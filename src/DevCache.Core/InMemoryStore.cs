using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace DevCache;

public sealed class InMemoryStore : IDisposable
{
    // =======================
    // Core Storage
    // =======================
    private readonly ConcurrentDictionary<string, ValueEntry> _data =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed class ValueEntry
    {
        public string Value = default!;
        public DateTime? ExpiryUtc;

        public string Type => "string";
        public int Size => Value.Length; // Value?.Length ?? 0;

        public long GetTtlSeconds()
        {
            if (!ExpiryUtc.HasValue) return -1;
            var ttl = (long)(ExpiryUtc.Value - DateTime.UtcNow).TotalSeconds;
            return ttl > 0 ? ttl : -2;
        }
    }

    // =======================
    // AOF Persistence
    // =======================
    private readonly string _aofPath;
    private StreamWriter? _aofWriter;
    private readonly object _aofLock = new();

    // =======================
    // Expiry Loop
    // =======================
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _expiryTask;

    // =======================
    // Constructor
    // =======================
    public InMemoryStore()
    {
        _aofPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevCache",
            "devcache.aof"
        );

        Directory.CreateDirectory(Path.GetDirectoryName(_aofPath)!);

        // 🔴 IMPORTANT: Load AOF BEFORE opening writer
        LoadAof();

        OpenAofWriter();

        _expiryTask = Task.Run(ExpiryLoop, _cts.Token);
    }

    // =======================
    // AOF Init
    // =======================
    private void OpenAofWriter()
    {
        _aofWriter = new StreamWriter(
            new FileStream(
                _aofPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite   // ✅ FIX: allow reading while writing
            ),
            Encoding.UTF8
        )
        {
            AutoFlush = true
        };

        _aofWriter.WriteLine($"# DevCache AOF started {DateTime.UtcNow:o}");
    }


    // =======================
    // AOF Load
    // =======================
    private void LoadAof()
    {
        if (!File.Exists(_aofPath))
        {
            Debug.WriteLine("[AOF] No existing file");
            return;
        }

        Debug.WriteLine("[AOF] Loading...");

        using var reader = new StreamReader(
            new FileStream(
                _aofPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite   // ✅ FIX: no file lock
            )
        );

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            ReplayCommand(line);
        }

        Debug.WriteLine($"[AOF] Load complete ({_data.Count} keys)");
    }

    private void ReplayCommand(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            return;

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;

        var cmd = parts[0].ToUpperInvariant();

        try
        {
            switch (cmd)
            {
                case "SET":
                    Set(parts[1], string.Join(" ", parts[2..]), persist: false);
                    break;

                case "DEL":
                    Del(parts[1], persist: false);
                    break;

                case "EXPIRE":
                    if (int.TryParse(parts[2], out var sec))
                        Expire(parts[1], sec, persist: false);
                    break;
            }
        }
        catch { /* ignore corrupted lines */ }
    }

    private void AppendToAof(string command)
    {
        lock (_aofLock)
        {
            _aofWriter?.WriteLine(command);
        }
    }

    // =======================
    // Expiry Loop
    // =======================
    private async Task ExpiryLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                foreach (var kvp in _data.ToArray())
                {
                    if (kvp.Value.ExpiryUtc <= now)
                        _data.TryRemove(kvp.Key, out _);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EXPIRY] {ex.Message}");
            }

            await Task.Delay(1000, _cts.Token);
        }
    }


    // =======================
    // Commands
    // =======================
    public bool Set(string key, string value, bool persist = true)
    {
        _data[key] = new ValueEntry { Value = value };

        if (persist)
            AppendToAof($"SET {key} {value}");

        return true;
    }

    public string? Get(string key)
    {
        if (!_data.TryGetValue(key, out var entry))
            return null;

        if (entry.ExpiryUtc <= DateTime.UtcNow)
        {
            _data.TryRemove(key, out _);
            return null;
        }

        return entry.Value;
    }

    public bool Del(string key, bool persist = true)
    {
        var removed = _data.TryRemove(key, out _);

        if (removed && persist)
            AppendToAof($"DEL {key}");

        return removed;
    }

    public bool Exists(string key) => Get(key) != null;

    public void FlushAll() => _data.Clear();

    public bool Expire(string key, int seconds, bool persist = true)
    {
        if (!_data.TryGetValue(key, out var entry))
            return false;

        entry.ExpiryUtc = DateTime.UtcNow.AddSeconds(seconds);

        if (persist)
            AppendToAof($"EXPIRE {key} {seconds}");

        return true;
    }

    public long TTL(string key)
    {
        if (!_data.TryGetValue(key, out var entry)) return -2;
        return entry.GetTtlSeconds();
    }

    // ---------------- UI / DataGrid Support ----------------

    public IEnumerable<string> Keys =>
        _data.Where(x => x.Value.ExpiryUtc == null || x.Value.ExpiryUtc > DateTime.UtcNow)
             .Select(x => x.Key);


    public bool TryGetMeta(string key, out CacheMeta meta)
    {
        meta = default!;
        if (!_data.TryGetValue(key, out var entry)) return false;

        meta = new CacheMeta
        {
            Type = entry.Type,
            TtlSeconds = (int)entry.GetTtlSeconds(),
            SizeBytes = entry.Size
        };
        return true;
    }

    // Optional: Return key-value pairs (for GetAllKeys command)
    public IReadOnlyDictionary<string, string> GetAllKeys()
    {
        var now = DateTime.UtcNow;
        return _data
            .Where(kvp => kvp.Value.ExpiryUtc == null || kvp.Value.ExpiryUtc > now)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CacheItem> GetAllCacheItems()
    {
        var now = DateTime.UtcNow;
        return _data
            .Where(kvp => kvp.Value.ExpiryUtc == null || kvp.Value.ExpiryUtc > now)
            .Select(kvp => new CacheItem
            {
                Key = kvp.Key,
                Value = kvp.Value.Value,
                Type = kvp.Value.Type,
                TtlSeconds = (int)kvp.Value.GetTtlSeconds(),
                SizeBytes = kvp.Value.Size
            })
            .ToList()
            .AsReadOnly();
    }

    // =======================
    // Dispose
    // =======================
    public void Dispose()
    {
        _cts.Cancel();

        try { _expiryTask.Wait(2000); } catch { }

        lock (_aofLock)
        {
            _aofWriter?.Dispose();
            _aofWriter = null;
        }
    }

}

public record CacheItem
{
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
    public string Type { get; init; } = "string";
    public int TtlSeconds { get; init; }
    public int SizeBytes { get; init; }
}
