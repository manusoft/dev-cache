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
            Debug.WriteLine("[AOF] No file");
            return;
        }

        Debug.WriteLine("[AOF] Loading RESP format...");

        int loaded = 0;

        using var fs = new FileStream(_aofPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            string? line = reader.ReadLine();
            if (line == null) break;

            line = line.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

            if (!line.StartsWith("*")) continue;

            if (!int.TryParse(line.Substring(1), out int argCount) || argCount < 1) continue;

            var commandArgs = new List<string>(argCount);
            bool valid = true;

            for (int i = 0; i < argCount && valid; i++)
            {
                string? lenLine = reader.ReadLine();
                if (lenLine == null || !lenLine.StartsWith("$"))
                {
                    valid = false;
                    continue;
                }

                if (!int.TryParse(lenLine.Substring(1), out int len))
                {
                    valid = false;
                    continue;
                }

                // ──────────────────────────────────────────────
                // Critical change: read EXACTLY len characters
                // (assuming UTF-8 stream, Read(len) gives correct chars)
                char[] buffer = new char[len];
                int readChars = reader.Read(buffer, 0, len);

                if (readChars != len)
                {
                    Debug.WriteLine($"[AOF] Short read for bulk string: expected {len} chars, got {readChars}");
                    valid = false;
                    continue;
                }

                string value = new string(buffer);

                // Consume the trailing \r\n after the bulk string
                string? terminator = reader.ReadLine();
                if (terminator == null || !string.IsNullOrEmpty(terminator.Trim()))
                {
                    // In strict RESP, should be empty line or just \r\n
                    // But many implementations are lenient
                    Debug.WriteLine($"[AOF] Unexpected terminator after bulk: '{terminator}'");
                    // You can choose to proceed or fail
                    // For now, proceed leniently
                }

                commandArgs.Add(value);
            }

            if (!valid || commandArgs.Count != argCount) continue;

            string cmd = commandArgs[0].ToUpperInvariant();

            try
            {
                switch (cmd)
                {
                    case "SET" when commandArgs.Count == 3:
                        Set(commandArgs[1], commandArgs[2], persist: false);
                        loaded++;
                        break;

                    case "DEL" when commandArgs.Count == 2:
                        Del(commandArgs[1]!, persist: false);
                        loaded++;
                        break;

                    case "EXPIRE" when commandArgs.Count == 3:
                        if (int.TryParse(commandArgs[2], out int sec))
                        {
                            Expire(commandArgs[1]!, sec, persist: false);
                            loaded++;
                        }
                        break;

                    case "FLUSHDB":
                        _data.Clear();
                        loaded++;
                        break;
                }
            }
            catch { }
        }

        Debug.WriteLine($"[AOF] Loaded {loaded} commands → {_data.Count} keys in memory");
    }

    //private void LoadAof()
    //{
    //    if (!File.Exists(_aofPath))
    //    {
    //        Debug.WriteLine("[AOF] No file");
    //        return;
    //    }

    //    Debug.WriteLine("[AOF] Loading RESP format...");

    //    using var fs = new FileStream(_aofPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    //    using var reader = new StreamReader(fs, Encoding.UTF8);

    //    int loaded = 0;

    //    while (!reader.EndOfStream)
    //    {
    //        string? line = reader.ReadLine();
    //        if (line == null) break;

    //        line = line.Trim();
    //        if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

    //        if (!line.StartsWith("*")) continue;  // skip invalid

    //        if (!int.TryParse(line.Substring(1), out int argCount) || argCount < 1) continue;

    //        var commandArgs = new List<string?>(argCount);

    //        bool valid = true;

    //        for (int i = 0; i < argCount; i++)
    //        {
    //            string? lenLine = reader.ReadLine();
    //            if (lenLine == null || !lenLine.StartsWith("$"))
    //            {
    //                valid = false;
    //                break;
    //            }

    //            if (!int.TryParse(lenLine.Substring(1), out int len))
    //            {
    //                valid = false;
    //                break;
    //            }

    //            string? value = reader.ReadLine();
    //            if (value == null || Encoding.UTF8.GetByteCount(value) != len)
    //            {
    //                valid = false;
    //                break;
    //            }

    //            commandArgs.Add(value);
    //        }

    //        if (!valid || commandArgs.Count != argCount) continue;

    //        // Now replay
    //        string cmd = commandArgs[0]!.ToUpperInvariant();

    //        try
    //        {
    //            switch (cmd)
    //            {
    //                case "SET" when commandArgs.Count == 3:
    //                    Set(commandArgs[1]!, commandArgs[2]!, persist: false);
    //                    loaded++;
    //                    break;

    //                case "DEL" when commandArgs.Count == 2:
    //                    Del(commandArgs[1]!, persist: false);
    //                    loaded++;
    //                    break;

    //                case "EXPIRE" when commandArgs.Count == 3:
    //                    if (int.TryParse(commandArgs[2], out int sec))
    //                    {
    //                        Expire(commandArgs[1]!, sec, persist: false);
    //                        loaded++;
    //                    }
    //                    break;

    //                case "FLUSHDB":
    //                    _data.Clear();
    //                    loaded++;
    //                    break;
    //            }
    //        }
    //        catch { /* skip bad command */ }
    //    }

    //    Debug.WriteLine($"[AOF] Loaded {loaded} commands");
    //}
    private void AppendRespCommand(string cmdName, params string[] args)
    {
        lock (_aofLock)
        {
            if (_aofWriter == null) return;

            // Write array header
            _aofWriter.WriteLine($"*{args.Length + 1}");  // +1 for command name

            // Command name
            WriteBulk(_aofWriter, cmdName);

            foreach (var arg in args)
                WriteBulk(_aofWriter, arg);

            _aofWriter.Flush();
        }
    }

    private static void WriteBulk(StreamWriter writer, string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetByteCount(s);
        writer.WriteLine($"${bytes}");
        writer.WriteLine(s);
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
            AppendRespCommand("SET", key, value);

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
            AppendRespCommand("DEL", key);

        return removed;
    }

    public bool Exists(string key) => Get(key) != null;

    public void FlushAll()
    {
        _data.Clear();

        // Make it survive restart
        lock (_aofLock)
        {
            if (_aofWriter != null)
            {
                _aofWriter.Dispose();
                _aofWriter = null;
            }

            // Truncate + write new header
            using (var fs = new FileStream(_aofPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(fs, Encoding.UTF8))
            {
                writer.WriteLine($"# DevCache AOF flushed & restarted {DateTime.UtcNow:o}");
            }

            OpenAofWriter();  // reopen for future writes
        }
    }

    public bool Expire(string key, int seconds, bool persist = true)
    {
        if (!_data.TryGetValue(key, out var entry))
            return false;

        entry.ExpiryUtc = DateTime.UtcNow.AddSeconds(seconds);

        if (persist)
            AppendRespCommand("EXPIRE", key, seconds.ToString());

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
