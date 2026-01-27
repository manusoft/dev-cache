using DevCache.Core.Eviction;
using DevCache.Core.Models;
using DevCache.Core.Persistence;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace DevCache.Core.Storage;

public sealed partial class InMemoryStore : IDisposable
{
    // Core Storage
    private readonly ConcurrentDictionary<string, ValueEntry> _data =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IEvictionPolicy _eviction;
    private readonly long _maxMemory;

    // Statistics
    private long _totalCommandsProcessed;
    private long _keyspaceHits;
    private long _keyspaceMisses;
    private long _expiredKeys;
    private long _evictedKeys;

    // For keyspace
    private long _keysWithExpiry;
    private long _totalTtlSumMs;
    private long _ttlSampleCount;

    // For OPS/sec calculation
    private long _commandsInCurrentSecond;
    private long _opsPerSecSnapshot;
    private DateTime _lastOpsSnapshot = DateTime.UtcNow;

    public long KeyCount => _data.Count;

    private readonly AofPersistence _aof;

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _expiryTask;

    public InMemoryStore()
    {
        string aofPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevCache",
            "devcache.aof"
        );

        Directory.CreateDirectory(Path.GetDirectoryName(aofPath)!);

        _maxMemory = 0; // overridden by runtime if needed

        _eviction = _maxMemory == 0 ? new NoEvictionPolicy() : new LruEvictionPolicy();

        _aof = new AofPersistence(this, aofPath);
        _aof.Load();

        _expiryTask = Task.Run(ExpiryLoop, _cts.Token);
    }

    // Internal access for AOF
    internal IEnumerable<KeyValuePair<string, ValueEntry>> GetAllEntries() => _data;

    internal ValueEntry? GetEntryInternal(string key) => _data.TryGetValue(key, out var entry) ? entry : null;


    private void EnsureMemoryAvailable(long incomingBytes)
    {
        if (_maxMemory <= 0) return;

        while (GetApproximateMemoryBytesUsed() + incomingBytes > _maxMemory)
        {
            var victim = _eviction.SelectEvictionCandidate();
            if (victim == null) break;

            if (_data.TryRemove(victim, out var entry))
            {
                _eviction.OnKeyRemove(victim);
                IncrementEvictedKeys();

                if (entry?.ExpiryUtc.HasValue == true)
                    OnExpiryRemoved();
            }
            else
            {
                break;
            }
        }
    }


    // ---------------- Background Tasks ----------------
    private async Task ExpiryLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;

                foreach (var kvp in _data.ToArray())
                {
                    if (kvp.Value.ExpiryUtc.HasValue && kvp.Value.ExpiryUtc <= now)
                    {
                        _data.TryRemove(kvp.Key, out _);
                        IncrementExpiredKeys();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EXPIRY] {ex.Message}");
            }

            await Task.Delay(200, _cts.Token); // Faster for testing
        }
    }

    // ---------------- Statistics ----------------
    public void IncrementHit() => Interlocked.Increment(ref _keyspaceHits);
    public void IncrementMiss() => Interlocked.Increment(ref _keyspaceMisses);
    public void IncrementCommandsProcessed()
    {
        Interlocked.Increment(ref _totalCommandsProcessed);

        var now = DateTime.UtcNow;
        if ((now - _lastOpsSnapshot).TotalSeconds >= 1.0)
        {
            _opsPerSecSnapshot = Interlocked.Exchange(ref _commandsInCurrentSecond, 0);
            _lastOpsSnapshot = now;
        }

        Interlocked.Increment(ref _commandsInCurrentSecond);
    }

    public long InstantaneousOpsPerSec => _opsPerSecSnapshot;

    public void IncrementExpiredKeys() => Interlocked.Increment(ref _expiredKeys);
    public void IncrementEvictedKeys() => Interlocked.Increment(ref _evictedKeys);

    public void OnExpiryAdded(long ttlMs)
    {
        Interlocked.Increment(ref _keysWithExpiry);
        Interlocked.Add(ref _totalTtlSumMs, ttlMs);
        Interlocked.Increment(ref _ttlSampleCount);
    }

    public void OnExpiryRemoved()
    {
        Interlocked.Decrement(ref _keysWithExpiry);
        // Approximation: no subtract from sum
    }

    public DbStatistics GetDbStatistics(int db = 0)
    {
        if (db != 0) throw new NotSupportedException("Only db0 supported");
        return new DbStatistics(
            KeyCount,
            Interlocked.Read(ref _keysWithExpiry),
            Interlocked.Read(ref _totalTtlSumMs),
            Interlocked.Read(ref _ttlSampleCount)
        );
    }

    public long GetApproximateMemoryBytesUsed()
    {
        long sum = _data.Count * 120; // per-key overhead

        foreach (var entry in _data.Values)
        {
            sum += entry.EstimatedSizeBytes;
        }

        sum += _keysWithExpiry * 48; // expiry overhead

        return sum;
    }

    public long TotalCommandsProcessed => Interlocked.Read(ref _totalCommandsProcessed);
    public long KeyspaceHits => Interlocked.Read(ref _keyspaceHits);
    public long KeyspaceMisses => Interlocked.Read(ref _keyspaceMisses);
    public long ExpiredKeys => Interlocked.Read(ref _expiredKeys);
    public long EvictedKeys => Interlocked.Read(ref _evictedKeys);

    public long AofFileSizeBytes => _aof.CurrentAofSize;

    // ---------------- Commands ----------------
    public bool Set(string key, string value, bool persist = true)
    {
        long estimate = value.Length * 2L + 64;
        EnsureMemoryAvailable(estimate);

        var entry = new StringEntry { Value = value };
        _data[key] = entry;

        _eviction.OnKeyInsert(key);

        if (persist)
            _aof.AppendCommand("SET", key, value);

        return true;
    }

    public string? Get(string key)
    {
        var entry = GetEntry(key);
        if (entry is StringEntry strEntry)
        {
            _eviction.OnKeyAccess(key);
            IncrementHit();
            return strEntry.Value;
        }
        IncrementMiss();
        return null;
    }

    public bool Del(string key, bool persist = true)
    {
        var removed = _data.TryRemove(key, out var entry);
        if (removed && entry?.ExpiryUtc.HasValue == true)
            OnExpiryRemoved();

        if (removed)
            _eviction.OnKeyRemove(key);

        if (removed && persist)
            _aof.AppendCommand("DEL", key);

        return removed;
    }

    public bool Exists(string key) => GetEntry(key) != null;

    public long Incr(string key, long increment)
    {
        var entry = GetEntry(key); // this already handles expiry

        if (entry == null)
        {
            // Key didn't exist → start from 0
            var newEntry = new StringEntry { Value = increment.ToString() };
            _data[key] = newEntry;
            _aof.AppendCommand("INCRBY", key, increment.ToString());
            return increment;
        }

        if (entry is not StringEntry strEntry)
        {
            throw new InvalidOperationException("WRONGTYPE Operation against a key holding the wrong kind of value");
        }

        // Key exists and is a string → must be parseable as integer
        if (!long.TryParse(strEntry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long current))
        {
            throw new InvalidOperationException("WRONGTYPE Operation against a key holding the wrong kind of value");
        }

        long newValue = current + increment;

        // Update value
        strEntry.Value = newValue.ToString(CultureInfo.InvariantCulture);

        // Persist
        _aof.AppendCommand("INCRBY", key, increment.ToString());

        return newValue;
    }

    public void FlushDb(bool persist = false)
    {
        _data.Clear();
        _keysWithExpiry = 0;
        _totalTtlSumMs = 0;
        _ttlSampleCount = 0;

        if (persist)
        {
            _aof.AppendCommand("FLUSHDB");
        }

        Debug.WriteLine("[FlushDb] Database cleared. " + (persist ? "Appended to AOF." : "No AOF append (load mode)."));
    }

    public void FlushAll(bool persist = false)
    {
        FlushDb(persist);

        if (persist)
        {
            _aof.AppendCommand("FLUSHALL");
        }

        Debug.WriteLine("[FlushAll] All cleared. " + (persist ? "Appended to AOF." : "No AOF append (load mode)."));
    }

    public bool Expire(string key, long milliseconds, bool persist = true)
    {
        var entry = GetEntry(key);
        if (entry == null) return false;

        var newExpiry = DateTimeOffset.UtcNow.AddMilliseconds(milliseconds);
        bool hadExpiry = entry.ExpiryUtc.HasValue;

        entry.ExpiryUtc = newExpiry;

        if (!hadExpiry)
            OnExpiryAdded(milliseconds);

        if (persist)
            _aof.AppendCommand("PEXPIRE", key, milliseconds.ToString());

        return true;
    }

    public long GetTtlSeconds(string key)
    {
        var entry = GetEntry(key);
        return entry?.GetTtlSeconds() ?? -2;
    }

    public long GetTtlMilliseconds(string key)
    {
        var entry = GetEntry(key);
        return entry?.GetTtlMilliseconds() ?? -2;
    }


    // ---------------- Lists ----------------
    public int LPush(string key, string[] values, bool persist = true)
    {
        var entry = GetOrCreateListEntry(key);
        _eviction.OnKeyInsert(key);

        int added = 0;
        for (int i = values.Length - 1; i >= 0; i--) // Reverse to push left
        {
            entry.Values.Insert(0, values[i]);
            added++;
        }

        if (persist)
            _aof.AppendCommand("LPUSH", [key, .. values]);

        return added;
    }

    public int RPush(string key, string[] values, bool persist = true)
    {
        var entry = GetOrCreateListEntry(key);
        _eviction.OnKeyInsert(key);

        int added = 0;
        foreach (var val in values)
        {
            entry.Values.Add(val);
            added++;
        }

        if (persist)
            _aof.AppendCommand("RPUSH", [key, .. values]);

        return added;
    }

    public string? LPop(string key, bool persist = true)
    {
        var entry = GetEntry(key) as ListEntry;
        if (entry == null || entry.Values.Count == 0) return null;

        string popped = entry.Values[0];
        entry.Values.RemoveAt(0);

        if (entry.Values.Count == 0)
            _data.TryRemove(key, out _);

        if (persist)
            _aof.AppendCommand("LPOP", key);

        return popped;
    }

    public string? RPop(string key, bool persist = true)
    {
        var entry = GetEntry(key) as ListEntry;
        if (entry == null || entry.Values.Count == 0) return null;

        string popped = entry.Values[^1];
        entry.Values.RemoveAt(entry.Values.Count - 1);

        if (entry.Values.Count == 0)
            _data.TryRemove(key, out _);

        if (persist)
            _aof.AppendCommand("RPOP", key);

        return popped;
    }

    public long LLen(string key)
    {
        var entry = GetEntry(key) as ListEntry;
        return entry?.Values.Count ?? 0;
    }

    // ---------------- Hashes ----------------
    public bool HSet(string key, string field, string value, bool persist = true)
    {
        var entry = GetOrCreateHashEntry(key);
        _eviction.OnKeyInsert(key);

        bool added = !entry.Fields.ContainsKey(field);
        entry.Fields[field] = value;

        if (persist)
            _aof.AppendCommand("HSET", key, field, value);

        return added;
    }

    public string? HGet(string key, string field)
    {
        var entry = GetEntry(key) as HashEntry;
        string? value = null;
        entry?.Fields.TryGetValue(field, out value);
        return value;
    }

    public bool HDel(string key, string field, bool persist = true)
    {
        var entry = GetEntry(key) as HashEntry;
        if (entry == null) return false;

        bool deleted = entry.Fields.Remove(field);

        if (deleted && entry.Fields.Count == 0)
            _data.TryRemove(key, out _);

        if (persist && deleted)
            _aof.AppendCommand("HDEL", key, field);

        return deleted;
    }

    public long HLen(string key)
    {
        var entry = GetEntry(key) as HashEntry;
        return entry?.Fields.Count ?? 0;
    }

    public IEnumerable<string> HKeys(string key)
    {
        var entry = GetEntry(key) as HashEntry;
        return entry?.Fields.Keys ?? Enumerable.Empty<string>();
    }

    public IEnumerable<string> HVals(string key)
    {
        var entry = GetEntry(key) as HashEntry;
        return entry?.Fields.Values ?? Enumerable.Empty<string>();
    }

    // ---------------- Helpers ----------------
    public ValueEntry? GetEntry(string key)
    {
        if (!_data.TryGetValue(key, out var entry))
            return null;

        if (entry.ExpiryUtc.HasValue && entry.ExpiryUtc <= DateTimeOffset.UtcNow)
        {
            _data.TryRemove(key, out _);
            _eviction.OnKeyRemove(key);
            OnExpiryRemoved();
            IncrementExpiredKeys();
            return null;
        }

        return entry;
    }

    private ListEntry GetOrCreateListEntry(string key)
    {
        return (ListEntry)_data.GetOrAdd(key, _ => new ListEntry());
    }

    private HashEntry GetOrCreateHashEntry(string key)
    {
        return (HashEntry)_data.GetOrAdd(key, _ => new HashEntry());
    }

    // ---------------- UI Support ----------------
    public IEnumerable<string> Keys =>
        _data.Where(x => !x.Value.ExpiryUtc.HasValue || x.Value.ExpiryUtc > DateTimeOffset.UtcNow)
             .Select(x => x.Key);

    public bool TryGetMeta(string key, out CacheMeta meta)
    {
        meta = default!;
        var entry = GetEntry(key);
        if (entry == null) return false;

        meta = new CacheMeta
        {
            Type = entry.Type,
            TtlSeconds = entry.GetTtlSeconds(),
            EstimatedSizeBytes = entry.EstimatedSizeBytes
        };
        return true;
    }

    public IReadOnlyList<CacheItem> GetAllCacheItems()
    {
        var now = DateTimeOffset.UtcNow;
        return _data
            .Where(kvp => !kvp.Value.ExpiryUtc.HasValue || kvp.Value.ExpiryUtc > now)
            .Select(kvp => new CacheItem
            {
                Key = kvp.Key,
                Value = kvp.Value is StringEntry s ? s.Value : "", // Extend for other types if needed
                Type = kvp.Value.Type,
                TtlSeconds = kvp.Value.GetTtlSeconds(),
                EstimatedSizeBytes = kvp.Value.EstimatedSizeBytes
            })
            .ToList()
            .AsReadOnly();
    }

    // ---------------- Dispose ----------------
    public void Dispose()
    {
        _cts.Cancel();
        try { _expiryTask.Wait(2000); } catch { }

        _aof.Dispose();
    }
}
