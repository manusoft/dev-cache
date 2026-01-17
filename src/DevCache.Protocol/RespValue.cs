namespace DevCache;

public sealed class RespValue
{
    public RespType Type { get; }
    public object? Value { get; }

    private RespValue(RespType type, object? value)
    {
        Type = type;
        Value = value;
    }

    public static RespValue Simple(string value) =>
        new(RespType.SimpleString, value);

    public static RespValue Error(string message) =>
        new(RespType.Error, message);

    public static RespValue Integer(long value) =>
        new(RespType.Integer, value);

    public static RespValue Bulk(string? value) =>
        new(value == null ? RespType.Null : RespType.BulkString, value);

    public static RespValue Array(IReadOnlyList<RespValue> items) =>
        new(RespType.Array, items);

    public static RespValue Null() =>
        new(RespType.Null, null);
}


public enum RespType
{
    SimpleString,
    Error,
    Integer,
    BulkString,
    Array,
    Null
}