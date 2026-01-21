using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace DevCache.Core;

public sealed class InMemoryStore : IDisposable
{
    private const long AUTO_REWRITE_MIN_SIZE_BYTES = 64 * 1024;      // 64 KB minimum size to consider rewrite
    private const int AUTO_REWRITE_GROWTH_PERCENT = 100;             // rewrite when file >= 100% larger than last rewrite
    private long _lastRewriteSize = 0;                               // track size after last rewrite

    // =======================
    // Core Storage
    // =======================
    private readonly ConcurrentDictionary<string, ValueEntry> _data =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed class ValueEntry
    {
        public string Value = default!;
        public DateTimeOffset? ExpiryUtc; // ← use DateTimeOffset for ms precision

        public string Type => "string";
        public int Size => Value.Length;

        public long GetTtlSeconds()
        {
            if (!ExpiryUtc.HasValue) return -1;
            var remaining = ExpiryUtc.Value - DateTimeOffset.UtcNow;
            return remaining.TotalSeconds > 0 ? (long)remaining.TotalSeconds : -2;
        }

        public long GetTtlMilliseconds()
        {
            if (!ExpiryUtc.HasValue) return -1;
            var remaining = ExpiryUtc.Value - DateTimeOffset.UtcNow;
            return remaining.TotalMilliseconds > 0 ? (long)remaining.TotalMilliseconds : -2;
        }
    }

    private readonly string _aofPath;
    private StreamWriter? _aofWriter;
    private readonly object _aofLock = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _expiryTask;
    private Task? _rewriteTask;

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
        _rewriteTask = Task.Run(AofRewriteMonitorLoop, _cts.Token);
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

        Debug.WriteLine("[AOF] Loading RESP AOF (byte-oriented)...");

        int loaded = 0;

        using var fs = new FileStream(_aofPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        byte[] buffer = new byte[4096]; // small working buffer
        int pos = 0;                    // current position in buffer
        int bytesInBuffer = 0;

        // Helper to read next line (\r\n terminated) from stream
        string? ReadNextLine()
        {
            var lineBytes = new List<byte>();
            while (true)
            {
                if (pos >= bytesInBuffer)
                {
                    bytesInBuffer = fs.Read(buffer, 0, buffer.Length);
                    if (bytesInBuffer == 0) return null;
                    pos = 0;
                }

                byte b = buffer[pos++];
                lineBytes.Add(b);

                if (b == '\n' && lineBytes.Count >= 2 && lineBytes[^2] == '\r')
                {
                    lineBytes.RemoveAt(lineBytes.Count - 1); // remove \n
                    lineBytes.RemoveAt(lineBytes.Count - 1); // remove \r
                    return Encoding.UTF8.GetString(lineBytes.ToArray());
                }
            }
        }

        // Helper to read exactly n bytes
        byte[]? ReadExactBytes(int count)
        {
            var result = new byte[count];
            int readTotal = 0;

            while (readTotal < count)
            {
                if (pos >= bytesInBuffer)
                {
                    bytesInBuffer = fs.Read(buffer, 0, buffer.Length);
                    if (bytesInBuffer == 0) return null;
                    pos = 0;
                }

                int toCopy = Math.Min(count - readTotal, bytesInBuffer - pos);
                Array.Copy(buffer, pos, result, readTotal, toCopy);
                pos += toCopy;
                readTotal += toCopy;
            }
            return result;
        }

        while (true)
        {
            string? line = ReadNextLine();
            if (line == null) break;

            line = line.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

            if (!line.StartsWith("*")) continue;

            if (!int.TryParse(line.Substring(1), out int argCount) || argCount < 1) continue;

            var commandArgs = new List<string>(argCount);
            bool valid = true;

            for (int i = 0; i < argCount && valid; i++)
            {
                string? lenLine = ReadNextLine();
                if (lenLine == null || !lenLine.StartsWith("$"))
                {
                    valid = false;
                    continue;
                }

                if (!int.TryParse(lenLine.Substring(1), out int byteLen) || byteLen < 0)
                {
                    valid = false;
                    continue;
                }

                byte[]? valueBytes = ReadExactBytes(byteLen);
                if (valueBytes == null || valueBytes.Length != byteLen)
                {
                    Debug.WriteLine($"[AOF] Short read for bulk: expected {byteLen} bytes");
                    valid = false;
                    continue;
                }

                string value = Encoding.UTF8.GetString(valueBytes);
                commandArgs.Add(value);

                // Consume the \r\n after bulk string
                byte[]? crlf = ReadExactBytes(2);
                if (crlf == null || crlf.Length != 2 || crlf[0] != '\r' || crlf[1] != '\n')
                {
                    Debug.WriteLine($"[AOF] Invalid or missing CRLF after bulk (read {crlf?.Length ?? 0} bytes)");
                    // Lenient: continue anyway for dev
                }
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
                        Del(commandArgs[1], persist: false);
                        loaded++;
                        break;

                    case "EXPIRE" when commandArgs.Count == 3:
                        if (int.TryParse(commandArgs[2], out int sec) && sec >= 0)
                        {
                            Expire(commandArgs[1], sec, persist: false);  // seconds overload
                            loaded++;
                        }
                        break;

                    case "PEXPIRE" when commandArgs.Count == 3:
                        if (long.TryParse(commandArgs[2], out long ms) && ms >= 0)
                        {
                            Expire(commandArgs[1], ms, persist: false);   // ms overload
                            loaded++;
                        }
                        break;

                    case "FLUSHDB":
                        _data.Clear();
                        loaded++;
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AOF] Failed replaying {cmd}: {ex.Message}");
            }
        }

        Debug.WriteLine($"[AOF] Loaded {loaded} commands → {_data.Count} keys, AOF file size: {new FileInfo(_aofPath).Length} bytes");
    }

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

            //await Task.Delay(1000, _cts.Token); //TODO: Revert to 1000 ms in production
            // Faster check for testing (adjust as needed)
            await Task.Delay(200, _cts.Token);  // ← 200 ms instead of 1000 ms
        }
    }

    private async Task AofRewriteMonitorLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(15000, _cts.Token); // ← increase to 15 seconds to give OS more time

                if (!File.Exists(_aofPath)) continue;

                long currentSize = 0;
                bool exists = false;

                try
                {
                    using var fs = new FileStream(_aofPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    currentSize = fs.Length;
                    exists = true;
                }
                catch (IOException ex)
                {
                    Debug.WriteLine($"[MONITOR] File access issue: {ex.Message} – retrying next cycle");
                    continue;
                }

                Debug.WriteLine($"[MONITOR] Raw check – exists: {exists}, size: {currentSize} bytes ({currentSize / 1024.0:F2} KB), min threshold: {AUTO_REWRITE_MIN_SIZE_BYTES} bytes");

                if (currentSize < AUTO_REWRITE_MIN_SIZE_BYTES)
                {
                    Debug.WriteLine("[MONITOR] Skipped – file too small");
                    continue;
                }

                if (currentSize <= _lastRewriteSize)
                {
                    Debug.WriteLine("[MONITOR] Size did not grow or shrank – skipping");
                    continue;
                }

                bool shouldRewrite =
                    _lastRewriteSize == 0 ||
                    currentSize >= _lastRewriteSize * (1 + AUTO_REWRITE_GROWTH_PERCENT / 100.0);

                // Optional safety (recommended):
                if (shouldRewrite && currentSize < _lastRewriteSize + 4096)
                {
                    Debug.WriteLine("[AOF] Skipping rewrite – size change too small");
                    continue;
                }

                if (shouldRewrite)
                {
                    double growth = ((double)currentSize / Math.Max(1, _lastRewriteSize) - 1) * 100;
                    Debug.WriteLine($"[AOF] Rewrite triggered – current: {currentSize / 1024.0:F2} KB, last: {_lastRewriteSize / 1024.0:F2} KB, growth: {growth:F1}%");
                    await RewriteAofAsync();
                }
                else
                {
                    Debug.WriteLine($"[MONITOR] No rewrite needed – current: {currentSize / 1024.0:F2} KB");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AOF Rewrite Monitor] {ex.Message}");
            }
        }
    }

    private async Task RewriteAofAsync()
    {
        string tempPath = _aofPath + ".rewrite.tmp";
        string backupPath = _aofPath + ".backup"; // optional extra safety

        StreamWriter? oldWriter = null;

        lock (_aofLock)
        {
            oldWriter = _aofWriter;
            _aofWriter = null; // prevent writes during rewrite
        }

        if (oldWriter != null)
        {
            try
            {
                await oldWriter.FlushAsync();
                oldWriter.Dispose();
            }
            catch { /* best effort */ }
        }


        try
        {
            // Optional: backup current AOF (good for paranoia)
            if (File.Exists(_aofPath)) File.Copy(_aofPath, backupPath, true);

            // 1. Write new compact AOF to temp            
            using (var tempFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var tempWriter = new StreamWriter(tempFs, Encoding.UTF8))
            {
                tempWriter.WriteLine($"# DevCache AOF rewritten {DateTime.UtcNow:o}");
                tempWriter.WriteLine(); // empty line for readability

                var currentData = _data.ToArray();

                Debug.WriteLine($"[REWRITE] Rewriting {currentData.Length} non-expired entries");

                int writtenCount = 0;

                foreach (var kvp in currentData)
                {
                    var entry = kvp.Value;

                    // Skip expired
                    if (entry.ExpiryUtc.HasValue && entry.ExpiryUtc <= DateTime.UtcNow)
                        continue;

                    // Write SET
                    await WriteRespArrayAsync(tempWriter,
                        "SET",
                        kvp.Key,
                        entry.Value
                    );

                    writtenCount++;

                    // Write EXPIRE if still valid TTL
                    if (entry.ExpiryUtc.HasValue)
                    {
                        long ttlSec = (long)(entry.ExpiryUtc.Value - DateTime.UtcNow).TotalSeconds;
                        if (ttlSec > 0)
                        {
                            await WriteRespArrayAsync(tempWriter,
                                "EXPIRE",
                                kvp.Key,
                                ttlSec.ToString()
                            );
                            writtenCount++;
                        }

                        Debug.WriteLine($"[REWRITE] Wrote key: {kvp.Key} (TTL: {ttlSec})");
                    }
                }

                await tempWriter.FlushAsync();
                Debug.WriteLine($"[REWRITE] Finished writing {writtenCount} commands");
            }


            lock (_aofLock)
            {
                // Backup old file (optional but helpful)
                if (File.Exists(_aofPath))
                {
                    string backup = _aofPath + ".old";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(_aofPath, backup, overwrite: true);
                }

                // Perform the move / copy
                File.Move(tempPath, _aofPath, overwrite: true);   // or use File.Copy + Delete(tempPath) if Move still causes issues

                // Flush file system cache by reopening and closing
                using (var fs = new FileStream(_aofPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    // just open/close to force refresh
                }

                // Get fresh FileInfo – do NOT reuse old instance
                var freshInfo = new FileInfo(_aofPath);

                if (!freshInfo.Exists)
                {
                    Debug.WriteLine("[AOF] CRITICAL: File disappeared after move!");
                    // Fallback: restore from .old if possible
                    if (File.Exists(_aofPath + ".old"))
                    {
                        File.Move(_aofPath + ".old", _aofPath, overwrite: true);
                        freshInfo = new FileInfo(_aofPath);
                    }
                }

                long newSize = freshInfo.Length;

                _lastRewriteSize = newSize;

                // Log EVERYTHING
                Debug.WriteLine($"[AOF] After move/copy:");
                Debug.WriteLine($"    Path: {freshInfo.FullName}");
                Debug.WriteLine($"    Exists: {freshInfo.Exists}");
                Debug.WriteLine($"    Size on disk: {newSize} bytes ({newSize / 1024.0:F2} KB)");
                Debug.WriteLine($"    _lastRewriteSize now set to: {_lastRewriteSize} bytes");

                OpenAofWriter(); // reopen for appends

                // Final confirmation log
                Debug.WriteLine($"[AOF] Rewrite completed – new size: {_lastRewriteSize / 1024.0:F2} KB");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AOF Rewrite] Failed: {ex.Message}");
            if (File.Exists(tempPath)) File.Delete(tempPath);

            // Critical: always recover writer
            lock (_aofLock)
            {
                if (_aofWriter == null)
                    OpenAofWriter();
            }
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

    // Overload for seconds (used by EX)
    public bool Expire(string key, int seconds, bool persist = true)
    {
        return Expire(key, (long)seconds * 1000, persist);
    }

    // Overload for milliseconds (used by PX and PEXPIRE)
    public bool Expire(string key, long milliseconds, bool persist = true)
    {
        if (!_data.TryGetValue(key, out var entry))
        {
            Debug.WriteLine($"[EXPIRE] Key not found: {key}");
            return false;
        }

        var newExpiry = DateTimeOffset.UtcNow.AddMilliseconds(milliseconds);
        entry.ExpiryUtc = newExpiry;

        Debug.WriteLine($"[EXPIRE] Set expiry for {key} to {newExpiry:o} (in {milliseconds} ms)");

        if (persist)
            AppendRespCommand("PEXPIRE", key, milliseconds.ToString()); 

        return true;
    }

    public long GetTtls(string key)
    {
        if (!_data.TryGetValue(key, out var entry)) return -2;
        return entry.GetTtlSeconds();
    }

    public long GetTtlMs(string key)
    {
        if (!_data.TryGetValue(key, out var entry)) return -2;
        return entry.GetTtlMilliseconds();
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
        try { _rewriteTask?.Wait(2000); } catch { }

        lock (_aofLock)
        {
            _aofWriter?.Dispose();
            _aofWriter = null;
        }
    }

    // Helper method (add this)
    private static async Task WriteRespArrayAsync(StreamWriter w, params string[] parts)
    {
        // parts = e.g. ["SET", "key", "value"] or ["EXPIRE", "key", "60"]
        await w.WriteLineAsync($"*{parts.Length}");

        foreach (var part in parts)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(part);
            await w.WriteLineAsync($"${bytes.Length}");
            await w.WriteAsync(part);           // write string directly
            await w.WriteLineAsync();           // \r\n after value
        }

        await w.FlushAsync();  // ensure data is written
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
