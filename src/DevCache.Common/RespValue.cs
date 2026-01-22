namespace DevCache.Common;

/// <summary>
/// High-level RESP value representation used by both reader and writer.
/// Supports all Redis-compatible RESP2 types used in DevCache.
/// </summary>
public sealed class RespValue
{
    public RespType Type { get; }
    public object? Value { get; }

    private RespValue(RespType type, object? value)
    {
        Type = type;
        Value = value;
    }

    // ────────────────────────────────────────────────
    // Factory methods – preferred way to create values
    // ────────────────────────────────────────────────

    public static RespValue SimpleString(string value)
        => new(RespType.SimpleString, value);

    public static RespValue Error(string message)
        => new(RespType.Error, message);

    public static RespValue Integer(long value)
        => new(RespType.Integer, value);

    public static RespValue BulkString(string? value)
        => value is null
            ? NullBulk
            : new(RespType.BulkString, value);

    public static RespValue NullBulk
        => new(RespType.NullBulk, null);

    public static RespValue Array(IReadOnlyList<RespValue> items)
        => new(RespType.Array, items);

    public static RespValue NullArray
        => new(RespType.NullArray, null);

    // Convenience helpers
    public bool IsNull => Type is RespType.NullBulk or RespType.NullArray;
    public string? AsString() => Value as string;
    public long? AsInteger() => Value as long?;
    public IReadOnlyList<RespValue>? AsArray() => Value as IReadOnlyList<RespValue>;
}

/// <summary>
/// RESP2 type discriminator
/// </summary>
public enum RespType
{
    SimpleString,
    Error,
    Integer,
    BulkString,
    NullBulk,
    Array,
    NullArray,
}