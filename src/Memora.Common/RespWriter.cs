using System.Text;

namespace ManuHub.Memora.Common;

/// <summary>
/// Writes RESP2 protocol to a stream (server → client direction)
/// </summary>
public sealed class RespWriter
{
    private static readonly byte[] CrLf = [(byte)'\r', (byte)'\n'];

    private readonly Stream _stream;

    public RespWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async Task WriteAsync(RespValue value, CancellationToken ct = default)
    {
        await WriteValueAsync(value, ct);
        await _stream.FlushAsync(ct);
    }

    private async Task WriteValueAsync(RespValue value, CancellationToken ct)
    {
        switch (value.Type)
        {
            case RespType.SimpleString:
                await WriteAsync($"+{value.Value}\r\n", ct);
                break;

            case RespType.Error:
                await WriteAsync($"-{value.Value}\r\n", ct);
                break;

            case RespType.Integer:
                await WriteAsync($":{value.Value}\r\n", ct);
                break;

            case RespType.BulkString:
                string s = (string)(value.Value ?? "");
                byte[] bytes = Encoding.UTF8.GetBytes(s);
                await WriteAsync($"${bytes.Length}\r\n", ct);
                await _stream.WriteAsync(bytes, ct);
                await _stream.WriteAsync(CrLf, ct);
                break;

            case RespType.NullBulk:
                await WriteAsync("$-1\r\n", ct);
                break;

            case RespType.Array:
                var items = (IReadOnlyList<RespValue>)value.Value!;
                await WriteAsync($"*{items.Count}\r\n", ct);
                foreach (var item in items)
                    await WriteValueAsync(item, ct);
                break;

            case RespType.NullArray:
                await WriteAsync("*-1\r\n", ct);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(value.Type));
        }
    }

    private async Task WriteAsync(string text, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await _stream.WriteAsync(bytes, ct);
    }

    public async Task WriteManyAsync(IEnumerable<RespValue> values, CancellationToken ct = default)
    {
        foreach (var v in values)
            await WriteValueAsync(v, ct);

        await _stream.FlushAsync(ct);
    }
}