using System.Text;

namespace DevCache.Common;

/// <summary>
/// Reads RESP2 protocol from a stream (TCP client → server direction)
/// </summary>
public sealed class RespReader
{
    private readonly Stream _stream;
    private readonly byte[] _singleByteBuffer = new byte[1];

    public RespReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async Task<RespValue?> ReadAsync(CancellationToken ct = default)
    {
        // Read first byte (type indicator)
        int read = await _stream.ReadAsync(_singleByteBuffer.AsMemory(0, 1), ct);
        if (read == 0) return null; // EOF / disconnect

        return _singleByteBuffer[0] switch
        {
            (byte)'+' => await ReadSimpleStringAsync(ct),
            (byte)'-' => await ReadErrorAsync(ct),
            (byte)':' => await ReadIntegerAsync(ct),
            (byte)'$' => await ReadBulkStringAsync(ct),
            (byte)'*' => await ReadArrayAsync(ct),
            _ => throw new InvalidOperationException($"Unknown RESP prefix: {(char)_singleByteBuffer[0]}")
        };
    }

    private async Task<RespValue> ReadSimpleStringAsync(CancellationToken ct)
         => RespValue.SimpleString(await ReadLineAsync(ct));

    private async Task<RespValue> ReadErrorAsync(CancellationToken ct)
        => RespValue.Error(await ReadLineAsync(ct));

    private async Task<RespValue> ReadIntegerAsync(CancellationToken ct)
    {
        string line = await ReadLineAsync(ct);
        if (!long.TryParse(line, out long val))
            throw new InvalidOperationException($"Invalid integer format: {line}");
        return RespValue.Integer(val);
    }

    private async Task<RespValue> ReadBulkStringAsync(CancellationToken ct)
    {
        string lenLine = await ReadLineAsync(ct);
        if (!int.TryParse(lenLine, out int length))
            throw new InvalidOperationException($"Invalid bulk length: {lenLine}");

        if (length == -1)
            return RespValue.NullBulk;

        byte[] data = new byte[length];
        await ReadExactAsync(data, ct);
        await ReadCrLfAsync(ct); // consume trailing \r\n

        string content = Encoding.UTF8.GetString(data);
        return RespValue.BulkString(content);
    }

    private async Task<RespValue> ReadArrayAsync(CancellationToken ct)
    {
        string countLine = await ReadLineAsync(ct);
        if (!int.TryParse(countLine, out int count))
            throw new InvalidOperationException($"Invalid array count: {countLine}");

        if (count == -1)
            return RespValue.NullArray;

        var items = new List<RespValue>(count);

        for (int i = 0; i < count; i++)
        {
            var item = await ReadAsync(ct);
            if (item is null)
                throw new IOException("Unexpected end of stream while reading array element");
            items.Add(item);
        }

        return RespValue.Array(items.AsReadOnly());
    }


    // ────────────────────────────────────────────────
    // Low-level helpers
    // ────────────────────────────────────────────────
    private async Task<string> ReadLineAsync(CancellationToken ct)
    {
        var sb = new StringBuilder(64);

        while (true)
        {
            int b = await ReadByteAsync(ct);

            if (b == '\r')
            {
                // Expect \n
                int next = await ReadByteAsync(ct);
                if (next != '\n')
                    throw new IOException("Expected \\n after \\r");
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
                throw new IOException("Unexpected end of stream while reading exact bytes");

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
        int read = await _stream.ReadAsync(_singleByteBuffer.AsMemory(0, 1), ct);
        if (read == 0)
            throw new IOException("Unexpected end of stream");

        return _singleByteBuffer[0];
    }
}