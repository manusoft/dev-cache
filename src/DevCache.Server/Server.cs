using DevCache.Common;
using DevCache.Core;
using DevCache.Core.Commands;
using DevCache.Core.Models;
using DevCache.Core.Storage;
using System.Net;
using System.Net.Sockets;

namespace DevCache.Server;

public sealed class Server : IDisposable
{
    private readonly ILogger _logger;
    private readonly IConfiguration _config;
    private TcpListener? _listener;
    private readonly InMemoryStore _store = new();
    private readonly DateTime _startedAt = DateTime.UtcNow;

    public string Bind { get; }
    public int Port { get; }

    public Server(IConfiguration config, ILogger logger)
    {
        _config = config;
        _logger =logger;       

        Bind = _config["DevCache:BindAddress"] ?? "127.0.0.1";
        Port = _config.GetValue<int>("DevCache:Port", 6380);

        CommandRegistry.Initialize(_store, GetRuntimeInfo());
    }

    public async Task StartAsync(CancellationToken token)
    {
        _listener = new TcpListener(IPAddress.Parse(Bind), Port);
        _listener.Start();

        _logger.LogInformation($"DevCache listening on {Bind}:{Port}");

        while (!token.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(token);
            _ = HandleClientAsync(client, token);   // pass token if you want to propagate
        }
    }

    public ServerRuntimeInfo GetRuntimeInfo()
    {
        return new ServerRuntimeInfo(
            Port: Port,
            StartedAt: _startedAt,
            ConfigFile: null, //_config["DevCache:ConfigFile"], // if you have such setting
            MaxMemoryBytes: 0 // or read from config later
        );
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        await using var stream = client.GetStream();

        var reader = new RespReader(stream);
        var writer = new RespWriter(stream);

        try
        {
            while (!token.IsCancellationRequested)
            {
                var request = await reader.ReadAsync(token);

                // Normal disconnect: client closed connection → ReadAsync returns null
                if (request == null)
                {
                    _logger.LogInformation("Client disconnected normally (EOF).");
                    break;
                }

                // Expect array: *N \r\n $cmd \r\n ... 
                if (request.Type != RespType.Array || request.Value is not IReadOnlyList<RespValue> items || items.Count == 0)
                {
                    await writer.WriteAsync(
                        RespValue.Error("Protocol error: expected non-empty array"),
                        token
                    );
                    continue;
                }

                var cmdResp = items[0];
                if (cmdResp.Type != RespType.BulkString && cmdResp.Type != RespType.SimpleString)
                {
                    await writer.WriteAsync(
                        RespValue.Error("Protocol error: command must be string"),
                        token
                    );
                    continue;
                }

                var commandName = (cmdResp.Value as string)?.ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(commandName))
                {
                    await writer.WriteAsync(
                        RespValue.Error("ERR empty command"),
                        token
                    );
                    continue;
                }

                _logger.LogInformation("Command: {Command}", commandName);

                CommandRegistry.Store.IncrementCommandsProcessed();

                var args = new List<string>(items.Count - 1);

                for (int i = 1; i < items.Count; i++)
                {
                    var arg = items[i];
                    if (arg.Type == RespType.BulkString || arg.Type == RespType.SimpleString)
                    {
                        args.Add(arg.Value as string ?? string.Empty);
                    }
                    else
                    {
                        args.Add(string.Empty); // fallback – or you can reject
                    }
                }

                if (!CommandRegistry.TryGet(commandName, out var commandHandler))
                {
                    await writer.WriteAsync(
                        RespValue.Error($"ERR unknown command '{commandName}'"),
                        token
                    );
                    continue;
                }

                var context = new CommandContext
                {
                    Client = client,
                    Stream = stream,
                    Writer = writer
                };

                try
                {
                    await commandHandler(context, args);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error executing command {Command}", commandName);

                    await writer.WriteAsync(
                        RespValue.Error($"ERR internal error: {ex.Message}"),
                        token
                    );
                }

            }
        }
        catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted })
        {
            // Friendly handling for common disconnect scenarios
            _logger.LogInformation("Client {RemoteEndPoint} disconnected normally.", client.Client.RemoteEndPoint);
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            // Unexpected error — log full details
            _logger.LogError(ex, "Unexpected client handling error");
        }
        finally
        {
            // No need to call client.Close() when using await using on stream
            // TcpClient will be disposed when it goes out of scope
            //client.Close();
            //_logger.LogDebug("Client connection closed.");
        }
    }

    public void Dispose()
    {
        _listener?.Stop();
        _store.Dispose();
    }
}
