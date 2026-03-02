namespace ManuHub.Memora.Models;

public abstract class ValueEntry
{
    public DateTimeOffset? ExpiryUtc { get; set; }

    public abstract string Type { get; }

    public abstract long EstimatedSizeBytes { get; }

    public long GetTtlSeconds()
    {
        if (!ExpiryUtc.HasValue) return -1;
        var remaining = ExpiryUtc.Value - DateTimeOffset.UtcNow;
        return remaining.TotalSeconds > 0 ? (long)remaining.TotalSeconds : -2;
    }

    public long GetTtlMilliseconds()
    {
        if (!ExpiryUtc.HasValue) return -1;
        var remaining = ExpiryUtc.Value - DateTimeOffset.UtcNow;
        return remaining.TotalMilliseconds > 0 ? (long)remaining.TotalMilliseconds : -2;
    }
}

public sealed class StringEntry : ValueEntry
{
    public string Value { get; set; } = "";

    public override string Type => "string";

    public override long EstimatedSizeBytes => Value.Length * 2L + 40; // UTF-16 + overhead
}

public sealed class ListEntry : ValueEntry
{
    public List<string> Values { get; } = new();

    public override string Type => "list";

    public override long EstimatedSizeBytes
    {
        get
        {
            long sum = 64; // list overhead
            foreach (var s in Values)
            {
                sum += s.Length * 2L + 40; // per string
            }
            sum += Values.Count * 24; // per element ref + padding
            return sum;
        }
    }
}

public sealed class HashEntry : ValueEntry
{
    public Dictionary<string, string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);

    public override string Type => "hash";

    public override long EstimatedSizeBytes
    {
        get
        {
            long sum = 64; // dict overhead
            foreach (var kv in Fields)
            {
                sum += kv.Key.Length * 2L + 40;
                sum += kv.Value.Length * 2L + 40;
            }
            sum += Fields.Count * 48; // per entry overhead
            return sum;
        }
    }
}