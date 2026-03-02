namespace ManuHub.Memora.Eviction;

public interface IEvictionPolicy
{
    void OnKeyAccess(string key);
    void OnKeyInsert(string key);
    void OnKeyRemove(string key);

    /// <summary>
    /// Returns a key to evict, or null if none available.
    /// </summary>
    string? SelectEvictionCandidate();
}
