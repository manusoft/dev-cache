using ManuHub.Memora;
using ManuHub.Memora.Commands;
using ManuHub.Memora.Common;
using ManuHub.Memora.Models;
using ManuHub.Memora.Storage;
using System.Net;
using System.Net.Sockets;

namespace Manuhub.Memora;

public sealed class MemoraServer : IDisposable
{
    private readonly ILogger _logger;
    private readonly IConfiguration _config;
    private TcpListener? _listener;
    private readonly InMemoryStore _store = new();
    private readonly DateTime _startedAt = DateTime.UtcNow;

    private string bind { get; }
    private int port { get; }

    public string? requirePass;

    public MemoraServer(IConfiguration config, ILogger logger)
    {
        _config = config;
        _logger =logger;       

        bind = _config["Memora:BindAddress"] ?? "127.0.0.1";
        port = _config.GetValue<int>("Memora:Port", 6380);
        requirePass = config["Memora:RequirePass"];

        CommandRegistry.Initialize(_store, GetRuntimeInfo(), requirePass);
    }

    public async Task StartAsync(CancellationToken token)
    {
        _listener = new TcpListener(IPAddress.Parse(bind), port);
        _listener.Start();

        _logger.LogInformation($"Memora listening on {bind}:{port}");

        while (!token.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(token);
            _ = HandleClientAsync(client, token);   // pass token if you want to propagate
        }
    }

    public ServerRuntimeInfo GetRuntimeInfo()
    {
        return new ServerRuntimeInfo(
            Port: port,
            StartedAt: _startedAt,
            ConfigFile: null, //_config["Memora:ConfigFile"], // if you have such setting
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

                var commandName = (cmdResp.Value as string)?.ToUpperInvariant() ?? "";
                if (string.IsNullOrWhiteSpace(commandName))
                {
                    await writer.WriteAsync(
                        RespValue.Error("ERR empty command"),
                        token
                    );
                    continue;
                }

                //_logger.LogInformation("Command: {Command}", commandName);
                _logger.LogDebug("Command: {Command}", commandName);

                CommandRegistry.Store.IncrementCommandsProcessed();

                // ────────────────────────────────────────────────
                // Proceed with normal execution
                // ────────────────────────────────────────────────

                var args = new List<string>(items.Count - 1);

                for (int i = 1; i < items.Count; i++)
                {
                    var item = items[i];
                    string? val = null;

                    switch (item.Type)
                    {
                        case RespType.BulkString:
                        case RespType.SimpleString:
                            val = item.AsString();  // this should return "" for $0
                            break;

                        case RespType.Integer:
                            val = item.AsInteger()?.ToString();
                            break;

                        case RespType.NullBulk:
                            val = "";  // explicitly handle null bulk as empty string
                            break;

                        default:
                            val = "";  // fallback for safety
                            break;
                    }

                    args.Add(val ?? "");                  
                }

                if (!CommandRegistry.TryGet(commandName, out var commandHandler))
                {
                    await writer.WriteAsync(
                        RespValue.Error($"ERR unknown command '{commandName}'"),
                        token
                    );
                    continue;
                }

                // ────────────────────────────────────────────────
                // Create context
                // ────────────────────────────────────────────────
                var context = new CommandContext
                {
                    Client = client,
                    Stream = stream,
                    Writer = writer
                };

                // ────────────────────────────────────────────────
                // Check authentication
                // ────────────────────────────────────────────────

                bool requiresAuth = commandName switch
                {
                    "AUTH" or "PING" or "QUIT" or "ECHO" or "INFO" or "ROLE" or "CLIENT" => false,
                    _ => true
                };

                if (requiresAuth && !context.IsAuthenticated && CommandRegistry.RequirePass != null)
                {
                    await writer.WriteAsync(
                        RespValue.Error("NOAUTH Authentication required."),
                        token);
                    continue;
                }

                // ────────────────────────────────────────────────
                // Execute command
                // ────────────────────────────────────────────────
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
