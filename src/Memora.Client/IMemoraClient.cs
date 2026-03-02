namespace ManuHub.Memora.Client;

/// <summary>
/// Interface for interacting with Memora cache server.
/// </summary>
public interface IMemoraClient : IAsyncDisposable
{
    // Strings
    Task<bool> SetAsync(string key, string value);
    Task<string?> GetAsync(string key);
    Task<long> IncrAsync(string key, long increment = 1);
    Task<long> DecrAsync(string key, long decrement = 1);
    Task<bool> DelAsync(string key);
    Task<bool> ExistsAsync(string key);
    Task<bool> ExpireAsync(string key, TimeSpan ttl);
    Task<long> TTLAsync(string key);

    // Lists
    Task<long> LPushAsync(string key, params string[] values);
    Task<long> RPushAsync(string key, params string[] values);
    Task<string?> LPopAsync(string key);
    Task<string?> RPopAsync(string key);
    Task<long> LLenAsync(string key);
    Task<string[]> LRangeAsync(string key, int start, int stop);

    // Hashes
    Task<int> HSetAsync(string key, params string[] fieldValuePairs);
    Task<string?> HGetAsync(string key, string field);
    Task<bool> HDelAsync(string key, string field);
    Task<long> HLenAsync(string key);
    Task<(string Field, string Value)[]> HGetAllAsync(string key);

    // DB Operations
    Task<bool> FlushDbAsync();
    Task<bool> FlushAllAsync();
    Task<string[]> KeysAsync(string pattern = "*");
}
