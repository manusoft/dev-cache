namespace DevCache.Core.Models;

public sealed class CacheMeta
{
    public string Type { get; init; } = "";
    public int TtlSeconds { get; init; }
    public int SizeBytes { get; init; }
}