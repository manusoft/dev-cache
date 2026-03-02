namespace ManuHub.Memora.Models;

public record CacheItem
{
    public string Key { get; init; } = "";
    public string Value { get; init; } = ""; // For strings; others may need extension
    public string Type { get; init; } = "string";
    public long TtlSeconds { get; init; }
    public long EstimatedSizeBytes { get; init; }
}