using System.Collections.Concurrent;

namespace DevCache;

public sealed class InMemoryStore
{
    private readonly ConcurrentDictionary<string, string> _data = new(StringComparer.OrdinalIgnoreCase);

    public bool Set(string key, string value)
    {
        _data[key] = value;
        return true;
    }

    public string? Get(string key)
    {
        _data.TryGetValue(key, out var value);
        return value;
    }

    public bool Del(string key)
    {
        return _data.TryRemove(key, out _);
    }

    public bool Exists(string key)
    {
        return _data.ContainsKey(key);
    }

    public void FlushAll()
    {
        _data.Clear();
    }
}