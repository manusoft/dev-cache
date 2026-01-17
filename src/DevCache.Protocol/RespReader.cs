using System.Text;

namespace DevCache;

public sealed class RespReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[1];

    public RespReader(Stream stream)
    {
        _stream = stream;
    }

    public async Task<RespValue?> ReadAsync(CancellationToken ct = default)
    {
        int read = await _stream.ReadAsync(_buffer, ct);
        if (read == 0)
            return null; // client disconnected

        return _buffer[0] switch
        {
            (byte)'+' => await ReadSimpleStringAsync(ct),
            (byte)'-' => await ReadErrorAsync(ct),
            (byte)':' => await ReadIntegerAsync(ct),
            (byte)'$' => await ReadBulkStringAsync(ct),
            (byte)'*' => await ReadArrayAsync(ct),
            _ => throw new InvalidOperationException("Unknown RESP prefix")
        };
    }

    private async Task<RespValue> ReadSimpleStringAsync(CancellationToken ct)
        => RespValue.Simple(await ReadLineAsync(ct));

    private async Task<RespValue> ReadErrorAsync(CancellationToken ct)
        => RespValue.Error(await ReadLineAsync(ct));

    private async Task<RespValue> ReadIntegerAsync(CancellationToken ct)
        => RespValue.Integer(long.Parse(await ReadLineAsync(ct)));

    private async Task<RespValue> ReadBulkStringAsync(CancellationToken ct)
    {
        int length = int.Parse(await ReadLineAsync(ct));
        if (length == -1)
            return RespValue.Null();

        byte[] data = new byte[length];
        await ReadExactAsync(data, ct);
        await ReadCrLfAsync(ct);

        return RespValue.Bulk(Encoding.UTF8.GetString(data));
    }

    private async Task<RespValue> ReadArrayAsync(CancellationToken ct)
    {
        int count = int.Parse(await ReadLineAsync(ct));
        if (count == -1)
            return RespValue.Null();

        var items = new List<RespValue>(count);
        for (int i = 0; i < count; i++)
        {
            var value = await ReadAsync(ct);
            if (value == null)
                throw new IOException("Unexpected end of stream");

            items.Add(value);
        }

        return RespValue.Array(items);
    }

    private async Task<string> ReadLineAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();

        while (true)
        {
            int b = await ReadByteAsync(ct);
            if (b == '\r')
            {
                await ReadByteAsync(ct); // \n
                break;
            }

            sb.Append((char)b);
        }

        return sb.ToString();
    }

    private async Task ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await _stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0)
                throw new IOException("Unexpected end of stream");

            offset += read;
        }
    }

    private async Task ReadCrLfAsync(CancellationToken ct)
    {
        await ReadByteAsync(ct); // \r
        await ReadByteAsync(ct); // \n
    }

    private async Task<int> ReadByteAsync(CancellationToken ct)
    {
        int read = await _stream.ReadAsync(_buffer, ct);
        if (read == 0)
            throw new IOException("Unexpected end of stream");

        return _buffer[0];
    }
}