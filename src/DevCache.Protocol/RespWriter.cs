using System.Text;

namespace DevCache;

public sealed class RespWriter
{
    private static readonly byte[] CrLf = "\r\n"u8.ToArray();

    public async Task WriteAsync(Stream stream, RespValue value, CancellationToken ct = default)
    {
        await WriteValueAsync(stream, value, ct);
        await stream.FlushAsync(ct);
    }

    private async Task WriteValueAsync(Stream stream, RespValue value, CancellationToken ct)
    {
        switch (value.Type)
        {
            case RespType.SimpleString:
                await WriteAsync(stream, $"+{value.Value}\r\n", ct);
                break;

            case RespType.Error:
                await WriteAsync(stream, $"-{value.Value}\r\n", ct);
                break;

            case RespType.Integer:
                await WriteAsync(stream, $":{value.Value}\r\n", ct);
                break;

            case RespType.BulkString:
                var str = (string)value.Value!;
                await WriteAsync(stream, $"${Encoding.UTF8.GetByteCount(str)}\r\n{str}\r\n", ct);
                break;

            case RespType.Array:
                var items = (IReadOnlyList<RespValue>)value.Value!;
                await WriteAsync(stream, $"*{items.Count}\r\n", ct);
                foreach (var item in items)
                    await WriteValueAsync(stream, item, ct);
                break;

            case RespType.Null:
                await WriteAsync(stream, "$-1\r\n", ct);
                break;
        }
    }

    private static async Task WriteAsync(Stream stream, string text, CancellationToken ct)
        => await stream.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
}