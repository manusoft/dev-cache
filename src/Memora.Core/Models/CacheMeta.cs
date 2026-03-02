namespace ManuHub.Memora.Models;

public sealed class CacheMeta
{
    public string Type { get; init; } = "";
    public long TtlSeconds { get; init; }
    public long EstimatedSizeBytes { get; init; }
}