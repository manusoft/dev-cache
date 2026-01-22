namespace DevCache.Core.Models;

public record CacheItem
{
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
    public string Type { get; init; } = "string";
    public int TtlSeconds { get; init; }
    public int SizeBytes { get; init; }
}
