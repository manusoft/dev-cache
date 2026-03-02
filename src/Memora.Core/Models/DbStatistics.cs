namespace ManuHub.Memora.Models;

public record DbStatistics(
    long KeyCount,
    long KeysWithExpiry,
    long? TotalTtlSumMilliseconds,   // null = no expires
    long TtlSampleCount
)
{
    public double? AverageTtlMs =>
        TtlSampleCount > 0 && TotalTtlSumMilliseconds.HasValue
            ? (double)TotalTtlSumMilliseconds.Value / TtlSampleCount
            : null;
}