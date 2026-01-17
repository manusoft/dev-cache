using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DevCache.UI;

public sealed class DevCacheClient /*: IDisposable*/
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly RespWriter _writer;
    private readonly RespReader _reader;

    private const string Host = "127.0.0.1";
    private const int Port = 6380;

    //public DevCacheClient(string host = "127.0.0.1", int port = 6380)
    //{
    //    _client = new TcpClient(AddressFamily.InterNetwork); //AddressFamily.InterNetwork
    //    _client.Connect(host, port);
    //    _stream = _client.GetStream();
    //    _writer = new RespWriter();
    //    _reader = new RespReader(_stream);
    //}

    //public void Dispose()
    //{
    //    _client.Close();
    //    _client.Dispose();
    //}

    // ---------------- Core Commands ----------------
    private async Task<RespValue?> SendAsync(string command, params string[] args)
    {
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(Host, Port);

            using var stream = client.GetStream();

            var writer = new RespWriter();
            var reader = new RespReader(stream);

            var payload = RespValue.Array(
                new[] { RespValue.Bulk(command) }
                    .Concat(args.Select(RespValue.Bulk))
                    .ToArray()
            );

            await writer.WriteAsync(stream, payload);
            return await reader.ReadAsync();
        }
        catch (SocketException)
        {
            return null; // ← IMPORTANT: swallow it
        }
    }


    //private async Task<RespValue?> SendAsync(string command, params string[] args)
    //{
    //    // Build array: [command, arg1, arg2...]
    //    var items = new List<RespValue> { RespValue.Bulk(command) };
    //    items.AddRange(args.Select(RespValue.Bulk));

    //    var payload = RespValue.Array(items.AsReadOnly()); // correct single-parameter Array

    //    // Send to server
    //    await _writer.WriteAsync(_stream, payload); // <- must flush internally

    //    // Wait for response
    //    var resp = await _reader.ReadAsync();
    //    return resp;
    //}

    public async Task<bool> SetAsync(string key, string value)
    {
        //var resp = await SendAsync("SET", key, value);
        //return resp?.Type == RespType.SimpleString && resp.Value?.ToString() == "OK";
        var resp = await SendAsync("SET", key, value);
        return resp?.Type == RespType.SimpleString;
    }

    public async Task<string?> GetAsync(string key)
    {
        var resp = await SendAsync("GET", key);
        if (resp == null || resp.Type == RespType.Null) return null;
        return resp.Value?.ToString();
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var resp = await SendAsync("DEL", key);
        return resp?.Type == RespType.Integer && resp.Value?.ToString() == "1";
    }

    public async Task<IEnumerable<string>> KeysAsync()
    {
        var resp = await SendAsync("KEYS"); // correct command

        if (resp?.Type != RespType.Array)
            return Enumerable.Empty<string>();

        var items = (IReadOnlyList<RespValue>)resp.Value!;
        return items.Select(v => v.Value?.ToString() ?? string.Empty);
    }


    public async Task<CacheMeta?> GetMetaAsync(string key)
    {
        var resp = await SendAsync("GETMETA", key);
        if (resp?.Type != RespType.Array)
            return null;

        var list = (IReadOnlyList<RespValue>)resp.Value!;
        return new CacheMeta
        {
            Type = list[0].Value?.ToString() ?? "string",
            TtlSeconds = Convert.ToInt32(list[1].Value),
            SizeBytes = Convert.ToInt32(list[2].Value)
        };
    }

    //public async Task<(bool Success, CacheMeta? Meta)> TryGetMetaAsync(string key)
    //{
    //    var resp = await SendAsync("GETMETA", key);
    //    if (resp?.Type != RespType.Array)
    //        return (false, null);

    //    var list = (IReadOnlyList<RespValue>)resp.Value!;
    //    var meta = new CacheMeta
    //    {
    //        Type = list[0].Value?.ToString() ?? "string",
    //        TtlSeconds = Convert.ToInt32(list[1].Value),
    //        SizeBytes = Convert.ToInt32(list[2].Value)
    //    };

    //    return (true, meta);
    //}

}
