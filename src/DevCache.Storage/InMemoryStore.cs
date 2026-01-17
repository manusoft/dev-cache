using System.Collections.Concurrent;

namespace DevCache;

public sealed class InMemoryStore
{
    private sealed class ValueEntry
    {
        public string Value = default!;
        public DateTime? Expiry; // UTC time
    }

    private readonly ConcurrentDictionary<string, ValueEntry> _data =
        new(StringComparer.OrdinalIgnoreCase);

    public InMemoryStore()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    foreach (var key in _data.Keys)
                    {
                        if (_data.TryGetValue(key, out var entry) &&
                            entry.Expiry.HasValue &&
                            entry.Expiry.Value <= now)
                        {
                            _data.TryRemove(key, out _);
                        }
                    }
                }
                catch { /* ignore */ }

                await Task.Delay(1000);
            }
        });
    }

    public bool Set(string key, string value)
    {
         _data[key] = new ValueEntry { Value = value, Expiry = null };
        return true;
    }

    public string? Get(string key)
    {
        if (!_data.TryGetValue(key, out var entry))
            return null;

        if (entry.Expiry.HasValue && entry.Expiry.Value <= DateTime.UtcNow)
        {
            _data.TryRemove(key, out _);
            return null;
        }

        return entry.Value;
    }

    public bool Del(string key) => _data.TryRemove(key, out _);

    public bool Exists(string key)
    {
        var entry = Get(key);
        return entry != null;
    }

    public void FlushAll() => _data.Clear();

    public bool Expire(string key, int seconds)
    {
        if (!_data.TryGetValue(key, out var entry))
            return false;

        entry.Expiry = DateTime.UtcNow.AddSeconds(seconds);
        return true;
    }

    public long TTL(string key)
    {
        if (!_data.TryGetValue(key, out var entry))
            return -2; // key does not exist

        if (!entry.Expiry.HasValue)
            return -1; // key exists but no expiry

        var ttl = (long)(entry.Expiry.Value - DateTime.UtcNow).TotalSeconds;
        return ttl > 0 ? ttl : -2; // expired
    }

    public IReadOnlyDictionary<string, string> GetAllKeys()
    {
        var now = DateTime.UtcNow;
        return _data
            .Where(kvp => kvp.Value.Expiry == null || kvp.Value.Expiry > now)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value, StringComparer.OrdinalIgnoreCase);
    }

}