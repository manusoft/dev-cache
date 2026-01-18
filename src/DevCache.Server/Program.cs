using DevCache;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

internal class Program
{
    private static async Task Main(string[] args)
    {
        bool background = args.Contains("--background", StringComparer.OrdinalIgnoreCase);

        // Simple console + file logger for now
        var logger = CreateSimpleLogger(background);

        logger.LogInformation("DevCache starting on 127.0.0.1:6380 ...");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            logger.LogInformation("Shutdown requested via Ctrl+C");
        };


        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            cts.Cancel();
            logger.LogInformation("Process exit requested");
        };

        try
        {
            await RunServerAsync(logger, cts.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Server stopped gracefully");
        }
        catch (Exception ex)
        {
            logger.LogError($"Fatal error: {ex.Message}", ex);
            throw;
        }
    }

    private static async Task RunServerAsync(ILogger logger, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Parse("127.0.0.1"), 6380);

        try
        {
            listener.Start();
            logger.LogInformation("Listening on 127.0.0.1:6380");

            while (!ct.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
                {
                    break;
                }

                // Fire-and-forget per client (but we can improve later)
                _ = HandleClientAsync(client, logger, ct);
            }
        }
        finally
        {
            listener.Stop();
            logger.LogInformation("Listener stopped");
        }
    }

    private static async Task HandleClientAsync(TcpClient client, ILogger logger, CancellationToken ct)
    {
        using var stream = client.GetStream();
        var reader = new RespReader(stream);
        var writer = new RespWriter();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var request = await reader.ReadAsync(ct);
                if (request == null) break; // client closed connection

                if (request.Type != RespType.Array)
                {
                    await writer.WriteAsync(stream, RespValue.Error("Protocol error: expected array"));
                    continue;
                }

                var items = (IReadOnlyList<RespValue>)request.Value!;
                var cmdName = ((string?)items[0].Value)?.ToUpperInvariant() ?? "";

                logger.LogDebug($"Command: {cmdName}");

                if (string.IsNullOrWhiteSpace(cmdName))
                {
                    await writer.WriteAsync(stream, RespValue.Error("ERR empty command"));
                    continue;
                }

                var args = items.Skip(1)
                    .Select(v => (string?)v.Value ?? "")
                    .ToList();

                if (!CommandRegistry.TryGet(cmdName, out var command))
                {
                    await writer.WriteAsync(stream, RespValue.Error($"ERR unknown command '{cmdName}'"));
                    continue;
                }

                var ctx = new CommandContext
                {
                    Client = client,
                    Stream = stream,
                    Writer = writer
                };

                await command(ctx, args);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError($"Client error: {ex.Message}", ex);
        }
        finally
        {
            client.Close();
        }
    }

    // Very simple logger (console)
    private static ILogger CreateSimpleLogger(bool background)
    {
        var factory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        });

        return factory.CreateLogger("DevCache");
    }
}