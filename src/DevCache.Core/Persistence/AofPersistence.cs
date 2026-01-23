using DevCache.Core.Models;
using DevCache.Core.Storage;
using System.Diagnostics;
using System.Text;

namespace DevCache.Core.Persistence;

public sealed class AofPersistence : IDisposable
{
    private const long AUTO_REWRITE_MIN_SIZE_BYTES = 64 * 1024;
    private const int AUTO_REWRITE_GROWTH_PERCENT = 100;
    private long _lastRewriteSize = 0;

    private readonly InMemoryStore _store;
    private readonly string _aofPath;
    private StreamWriter? _aofWriter;
    private readonly object _aofLock = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _rewriteTask;
    private readonly Task _flushTask;
    private readonly CancellationTokenSource _flushCts = new();
    private volatile bool _needsFlush = false;

    public AofPersistence(InMemoryStore store, string aofPath)
    {
        _store = store;
        _aofPath = aofPath;

        OpenWriter();

        _rewriteTask = Task.Run(AofRewriteMonitorLoop, _cts.Token);
        _flushTask = Task.Run(BackgroundFlushLoop, _flushCts.Token);
    }

    public void Load()
    {
        if (!File.Exists(_aofPath))
        {
            Debug.WriteLine("[AOF] No file");
            return;
        }

        Debug.WriteLine("[AOF] Loading RESP AOF...");

        int loaded = 0;

        using var fs = new FileStream(_aofPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        byte[] buffer = new byte[4096];
        int pos = 0;
        int bytesInBuffer = 0;

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
                    lineBytes.RemoveAt(lineBytes.Count - 1);
                    lineBytes.RemoveAt(lineBytes.Count - 1);
                    return Encoding.UTF8.GetString(lineBytes.ToArray());
                }
            }
        }

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

            if (!int.TryParse(line[1..], out int argCount) || argCount < 1) continue;

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

                if (!int.TryParse(lenLine[1..], out int byteLen) || byteLen < 0)
                {
                    valid = false;
                    continue;
                }

                byte[]? valueBytes = ReadExactBytes(byteLen);
                if (valueBytes == null || valueBytes.Length != byteLen)
                {
                    valid = false;
                    continue;
                }

                string value = Encoding.UTF8.GetString(valueBytes);
                commandArgs.Add(value);

                ReadExactBytes(2); // CRLF
            }

            if (!valid || commandArgs.Count != argCount) continue;

            string cmd = commandArgs[0].ToUpperInvariant();

            try
            {
                switch (cmd)
                {
                    case "FLUSHDB":
                    case "FLUSHALL":
                        _store.FlushDb(persist: false);
                        loaded++;
                        break;  // just count as loaded, do NOT clear anything

                    case "SET" when commandArgs.Count == 3:
                        _store.Set(commandArgs[1], commandArgs[2], persist: false);
                        loaded++;
                        break;

                    case "DEL" when commandArgs.Count == 2:
                        _store.Del(commandArgs[1], persist: false);
                        loaded++;
                        break;

                    case "PEXPIRE" when commandArgs.Count == 3:
                        if (long.TryParse(commandArgs[2], out long ms) && ms >= 0)
                        {
                            _store.Expire(commandArgs[1], ms, persist: false);
                            loaded++;
                        }
                        break;

                    case "EXPIRE" when commandArgs.Count == 3:
                        if (int.TryParse(commandArgs[2], out int sec) && sec >= 0)
                        {
                            _store.Expire(commandArgs[1], sec * 1000L, persist: false);
                            loaded++;
                        }
                        break;

                    case "LPUSH" when commandArgs.Count >= 3:
                        _store.LPush(commandArgs[1], commandArgs.Skip(2).ToArray(), persist: false);
                        loaded++;
                        break;

                    case "RPUSH" when commandArgs.Count >= 3:
                        _store.RPush(commandArgs[1], commandArgs.Skip(2).ToArray(), persist: false);
                        loaded++;
                        break;

                    case "LPOP" when commandArgs.Count == 2:
                        _store.LPop(commandArgs[1], persist: false);
                        loaded++;
                        break;

                    case "RPOP" when commandArgs.Count == 2:
                        _store.RPop(commandArgs[1], persist: false);
                        loaded++;
                        break;

                    case "HSET" when commandArgs.Count == 4:
                        _store.HSet(commandArgs[1], commandArgs[2], commandArgs[3], persist: false);
                        loaded++;
                        break;

                    case "HDEL" when commandArgs.Count == 3:
                        _store.HDel(commandArgs[1], commandArgs[2], persist: false);
                        loaded++;
                        break;

                        // Add future commands here...
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AOF] Failed replaying {cmd}: {ex.Message}");
            }
        }

        Debug.WriteLine($"[AOF] Loaded {loaded} commands → {_store.KeyCount} keys");
    }

    public void AppendCommand(string cmdName, params string[] args)
    {
        lock (_aofLock)
        {
            if (_aofWriter == null) return;

            _aofWriter.WriteLine($"*{args.Length + 1}");

            WriteBulk(_aofWriter, cmdName);

            foreach (var arg in args)
                WriteBulk(_aofWriter, arg);

            _needsFlush = true;
        }
    }

    private static void WriteBulk(StreamWriter writer, string s)
    {
        var bytes = Encoding.UTF8.GetByteCount(s);
        writer.WriteLine($"${bytes}");
        writer.WriteLine(s);
    }

    private void OpenWriter()
    {
        _aofWriter = new StreamWriter(
            new FileStream(
                _aofPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite
            ),
            Encoding.UTF8
        )
        {
            AutoFlush = false
        };

        _aofWriter.WriteLine($"# DevCache AOF started {DateTime.UtcNow:o}");
    }

    private async Task AofRewriteMonitorLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            await Task.Delay(15000, _cts.Token);

            if (!File.Exists(_aofPath)) continue;

            long currentSize = new FileInfo(_aofPath).Length;

            if (currentSize < AUTO_REWRITE_MIN_SIZE_BYTES) continue;

            if (currentSize > _lastRewriteSize * (1 + AUTO_REWRITE_GROWTH_PERCENT / 100.0))
            {
                await RewriteAsync();
            }
        }
    }

    private async Task RewriteAsync()
    {
        string tempPath = _aofPath + ".rewrite.tmp";

        StreamWriter? oldWriter = null;

        lock (_aofLock)
        {
            oldWriter = _aofWriter;
            _aofWriter = null;
        }

        if (oldWriter != null)
        {
            await oldWriter.FlushAsync();
            oldWriter.Dispose();
        }

        try
        {
            using var tempFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write);
            using var tempWriter = new StreamWriter(tempFs, Encoding.UTF8);

            tempWriter.WriteLine($"# DevCache AOF rewritten {DateTime.UtcNow:o}");

            var now = DateTimeOffset.UtcNow;
            foreach (var kvp in _store.GetAllEntries())
            {
                var entry = kvp.Value;

                if (entry.ExpiryUtc.HasValue && entry.ExpiryUtc <= now) continue;

                switch (entry)
                {
                    case StringEntry str:
                        await WriteRespArrayAsync(tempWriter, "SET", kvp.Key, str.Value);
                        break;

                    case ListEntry lst:
                        if (lst.Values.Count > 0)
                        {
                            await WriteRespArrayAsync(tempWriter, "RPUSH", [kvp.Key, .. lst.Values]);
                        }
                        break;

                    case HashEntry hsh:
                        if (hsh.Fields.Count > 0)
                        {
                            var flat = new List<string> { kvp.Key };
                            foreach (var f in hsh.Fields)
                            {
                                flat.Add(f.Key);
                                flat.Add(f.Value);
                            }
                            await WriteRespArrayAsync(tempWriter, "HMSET", flat.ToArray());
                        }
                        break;
                }

                if (entry.ExpiryUtc.HasValue)
                {
                    long ttlMs = (long)(entry.ExpiryUtc.Value - now).TotalMilliseconds;
                    if (ttlMs > 0)
                    {
                        await WriteRespArrayAsync(tempWriter, "PEXPIRE", kvp.Key, ttlMs.ToString());
                    }
                }
            }

            await tempWriter.FlushAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AOF Rewrite] Failed: {ex.Message}");
            if (File.Exists(tempPath)) File.Delete(tempPath);
            return;
        }

        lock (_aofLock)
        {
            if (File.Exists(_aofPath))
            {
                string old = _aofPath + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(_aofPath, old);
            }

            File.Move(tempPath, _aofPath);

            _lastRewriteSize = new FileInfo(_aofPath).Length;

            OpenWriter();
        }
    }

    private async Task BackgroundFlushLoop()
    {
        while (!_flushCts.IsCancellationRequested)
        {
            await Task.Delay(300, _flushCts.Token);

            bool shouldFlush = false;
            lock (_aofLock)
            {
                if (_aofWriter != null && _needsFlush)
                {
                    shouldFlush = true;
                    _needsFlush = false;
                }
            }

            if (shouldFlush)
            {
                await _aofWriter!.FlushAsync();
            }
        }
    }

    public void FlushAndReset()
    {
        lock (_aofLock)
        {
            if (_aofWriter != null)
            {
                _aofWriter.Flush();
                _aofWriter.Dispose();
                _aofWriter = null;
            }

            using var fs = new FileStream(_aofPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(fs, Encoding.UTF8);
            writer.WriteLine($"# DevCache AOF flushed & restarted {DateTime.UtcNow:o}");
        }

        OpenWriter();
        _needsFlush = false;
    }

    public long CurrentAofSize
    {
        get
        {
            try { return File.Exists(_aofPath) ? new FileInfo(_aofPath).Length : 0; }
            catch { return 0; }
        }
    }

    private static async Task WriteRespArrayAsync(StreamWriter w, string cmd, params string[] args)
    {
        await w.WriteLineAsync($"*{args.Length + 1}");
        await w.WriteLineAsync($"${Encoding.UTF8.GetByteCount(cmd)}");
        await w.WriteLineAsync(cmd);

        foreach (var arg in args)
        {
            await w.WriteLineAsync($"${Encoding.UTF8.GetByteCount(arg)}");
            await w.WriteLineAsync(arg);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _flushCts.Cancel();

        try { _rewriteTask.Wait(2000); } catch { }
        try { _flushTask.Wait(2000); } catch { }

        lock (_aofLock)
        {
            if (_aofWriter != null)
            {
                _aofWriter.Flush();
                _aofWriter.Dispose();
                _aofWriter = null;
            }
        }
    }

}
