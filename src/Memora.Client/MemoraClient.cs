using ManuHub.Memora.Exceptions;
using System.Buffers;
using System.Net.Sockets;
using System.Text;

namespace ManuHub.Memora.Client;

/// <summary>
/// Async Memora client for interacting with the Memora cache server.
/// </summary>
public class MemoraClient : IMemoraClient
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly byte[] _buffer;

    /// <summary>
    /// Connects to a Memora server.
    /// </summary>
    /// <param name="host">Server host, default 127.0.0.1</param>
    /// <param name="port">Server port, default 6380</param>
    public MemoraClient(string host = "127.0.0.1", int port = 6380)
    {
        _tcp = new TcpClient();
        _tcp.Connect(host, port);
        _stream = _tcp.GetStream();
        _buffer = ArrayPool<byte>.Shared.Rent(8192);
    }

    #region Strings
    public async Task<bool> SetAsync(string key, string value)
    {
        string resp = await SendCommandAsync("SET", key, value);
        return !resp.StartsWith("ERR");
    }

    public async Task<string?> GetAsync(string key)
    {
        string resp = await SendCommandAsync("GET", key);
        if (resp.StartsWith("ERR")) return null;
        return resp;
    }

    public async Task<long> IncrAsync(string key, long increment = 1)
    {
        if (increment == 1)
            return long.Parse(await SendCommandAsync("INCR", key));
        else
            return long.Parse(await SendCommandAsync("INCRBY", key, increment.ToString()));
    }

    public async Task<long> DecrAsync(string key, long decrement = 1)
    {
        if (decrement == 1)
            return long.Parse(await SendCommandAsync("DECR", key));
        else
            return long.Parse(await SendCommandAsync("DECRBY", key, decrement.ToString()));
    }

    public async Task<bool> DelAsync(string key) =>
        long.Parse(await SendCommandAsync("DEL", key)) > 0;

    public async Task<bool> ExistsAsync(string key) =>
        long.Parse(await SendCommandAsync("EXISTS", key)) > 0;

    public async Task<bool> ExpireAsync(string key, TimeSpan ttl) =>
        long.Parse(await SendCommandAsync("EXPIRE", key, ((long)ttl.TotalSeconds).ToString())) > 0;

    public async Task<long> TTLAsync(string key) =>
        long.Parse(await SendCommandAsync("TTL", key));
    #endregion

    #region Lists
    public async Task<long> LPushAsync(string key, params string[] values)
    {
        string[] args = new[] { key }.Concat(values).ToArray();
        return long.Parse(await SendCommandAsync("LPUSH", args));
    }

    public async Task<long> RPushAsync(string key, params string[] values)
    {
        string[] args = new[] { key }.Concat(values).ToArray();
        return long.Parse(await SendCommandAsync("RPUSH", args));
    }

    public async Task<string?> LPopAsync(string key)
    {
        string resp = await SendCommandAsync("LPOP", key);
        return resp.StartsWith("ERR") ? null : resp;
    }

    public async Task<string?> RPopAsync(string key)
    {
        string resp = await SendCommandAsync("RPOP", key);
        return resp.StartsWith("ERR") ? null : resp;
    }

    public async Task<long> LLenAsync(string key) =>
        long.Parse(await SendCommandAsync("LLEN", key));

    public async Task<string[]> LRangeAsync(string key, int start, int stop)
    {
        string resp = await SendCommandAsync("LRANGE", key, start.ToString(), stop.ToString());
        return resp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    }
    #endregion

    #region Hashes
    public async Task<int> HSetAsync(string key, params string[] fieldValuePairs)
    {
        string[] args = new[] { key }.Concat(fieldValuePairs).ToArray();
        return int.Parse(await SendCommandAsync("HSET", args));
    }

    public async Task<string?> HGetAsync(string key, string field)
    {
        string resp = await SendCommandAsync("HGET", key, field);
        return resp.StartsWith("ERR") ? null : resp;
    }

    public async Task<bool> HDelAsync(string key, string field) =>
        long.Parse(await SendCommandAsync("HDEL", key, field)) > 0;

    public async Task<long> HLenAsync(string key) =>
        long.Parse(await SendCommandAsync("HLEN", key));

    public async Task<(string Field, string Value)[]> HGetAllAsync(string key)
    {
        string resp = await SendCommandAsync("HGETALL", key);
        var lines = resp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var result = new (string, string)[lines.Length / 2];
        for (int i = 0; i < lines.Length; i += 2)
            result[i / 2] = (lines[i], lines[i + 1]);
        return result;
    }
    #endregion

    #region DB Operations
    public async Task<bool> FlushDbAsync() =>
        !(await SendCommandAsync("FLUSHDB")).StartsWith("ERR");

    public async Task<bool> FlushAllAsync() =>
        !(await SendCommandAsync("FLUSHALL")).StartsWith("ERR");

    public async Task<string[]> KeysAsync(string pattern = "*")
    {
        string resp = await SendCommandAsync("KEYS", pattern);
        return resp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    }
    #endregion

    #region Core Send/Receive
    private async Task<string> SendCommandAsync(string command, params string[] args)
    {
        byte[] request = Encoding.UTF8.GetBytes(BuildResp(command, args));
        await _stream.WriteAsync(request, 0, request.Length);

        using var ms = new MemoryStream();
        int bytesRead;
        do
        {
            bytesRead = await _stream.ReadAsync(_buffer, 0, _buffer.Length);
            ms.Write(_buffer, 0, bytesRead);
        } while (_stream.DataAvailable);

        string resp = Encoding.UTF8.GetString(ms.ToArray()).Trim();

        if (resp.StartsWith("ERR"))
            throw new MemoraException(resp);

        return resp;
    }

    private static string BuildResp(string command, params string[] args)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"*{args.Length + 1}");
        sb.AppendLine($"${Encoding.UTF8.GetByteCount(command)}");
        sb.AppendLine(command);
        foreach (var arg in args)
        {
            sb.AppendLine($"${Encoding.UTF8.GetByteCount(arg)}");
            sb.AppendLine(arg);
        }
        return sb.ToString();
    }
    #endregion

    #region Dispose
    public ValueTask DisposeAsync()
    {
        _stream?.Dispose();
        _tcp?.Close();
        ArrayPool<byte>.Shared.Return(_buffer);
        return ValueTask.CompletedTask;
    }
    #endregion
}