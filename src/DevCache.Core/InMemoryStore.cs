using System.Collections.Concurrent;

namespace DevCache;

public sealed class InMemoryStore
{
    private sealed class ValueEntry
    {
        public string Value = default!;
        public DateTime? ExpiryUtc; // UTC time
        public string Type => "string"; // for future type support
        public int Size => Value?.Length ?? 0;

        public long GetTtlSeconds()
        {
            if (!ExpiryUtc.HasValue) return -1;
            var ttl = (long)(ExpiryUtc.Value - DateTime.UtcNow).TotalSeconds;
            return ttl > 0 ? ttl : -2;
        }
    }

    private readonly ConcurrentDictionary<string, ValueEntry> _data =
        new(StringComparer.OrdinalIgnoreCase);

    public InMemoryStore()
    {
        // Background cleanup task
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    foreach (var kvp in _data)
                    {
                        if (kvp.Value.ExpiryUtc.HasValue &&
                            kvp.Value.ExpiryUtc.Value <= now)
                        {
                            _data.TryRemove(kvp.Key, out _);
                        }
                    }
                }
                catch { /* ignore */ }

                await Task.Delay(1000);
            }
        });
    }


    // ---------------- Core KV ----------------
    public bool Set(string key, string value)
    {
        _data[key] = new ValueEntry { Value = value, ExpiryUtc = null };
        return true;
    }

    public string? Get(string key)
    {
        if (!_data.TryGetValue(key, out var entry)) return null;
        if (entry.ExpiryUtc.HasValue && entry.ExpiryUtc.Value <= DateTime.UtcNow)
        {
            _data.TryRemove(key, out _);
            return null;
        }

        return entry.Value;
    }

    public bool Del(string key) => _data.TryRemove(key, out _);

    public bool Exists(string key) => Get(key) != null;

    public void FlushAll() => _data.Clear();

    public bool Expire(string key, int seconds)
    {
        if (!_data.TryGetValue(key, out var entry)) return false;
        entry.ExpiryUtc = DateTime.UtcNow.AddSeconds(seconds);
        return true;
    }

    public long TTL(string key)
    {
        if (!_data.TryGetValue(key, out var entry)) return -2;
        return entry.GetTtlSeconds();
    }

    // ---------------- UI / DataGrid Support ----------------

    public IEnumerable<string> Keys
    {
        get
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _data)
            {
                if (kvp.Value.ExpiryUtc == null || kvp.Value.ExpiryUtc > now)
                    yield return kvp.Key;
            }
        }
    }


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

}

public record CacheItem
{
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
    public string Type { get; init; } = "string";
    public int TtlSeconds { get; init; }
    public int SizeBytes { get; init; }
}
