namespace ManuHub.Memora.Eviction;

public sealed class LruEvictionPolicy : IEvictionPolicy
{
    private readonly LinkedList<string> _lru = new();
    private readonly Dictionary<string, LinkedListNode<string>> _nodes =
        new(StringComparer.OrdinalIgnoreCase);

    public void OnKeyAccess(string key)
    {
        if (_nodes.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
        }
    }

    public void OnKeyInsert(string key)
    {
        if (_nodes.ContainsKey(key)) return;

        var node = _lru.AddFirst(key);
        _nodes[key] = node;
    }

    public void OnKeyRemove(string key)
    {
        if (_nodes.Remove(key, out var node))
        {
            _lru.Remove(node);
        }
    }

    public string? SelectEvictionCandidate()
        => _lru.Last?.Value;
}