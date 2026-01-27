namespace DevCache.Core.Eviction;

public sealed class NoEvictionPolicy : IEvictionPolicy
{
    public void OnKeyAccess(string key) { }
    public void OnKeyInsert(string key) { }
    public void OnKeyRemove(string key) { }

    public string? SelectEvictionCandidate() => null;
}