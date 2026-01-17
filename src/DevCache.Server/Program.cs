using DevCache;
using System.Net;
using System.Net.Sockets;


// Quick tests
// DevCache.Protocol

Console.WriteLine("DevCache starting on port 6379...");

var listener = new TcpListener(IPAddress.Loopback, 6379);
listener.Start();

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = HandleClientAsync(client);
}

static async Task HandleClientAsync(TcpClient client)
{
    using var stream = client.GetStream();

    var reader = new RespReader(stream);
    var writer = new RespWriter();

    try
    {
        while (true)
        {
            var request = await reader.ReadAsync();
            if (request == null)
                break;

            // Expect array: [command, arg1, arg2...]
            if (request.Type != RespType.Array)
            {
                await writer.WriteAsync(stream,
                    RespValue.Error("Protocol error: expected array"));
                continue;
            }

            var items = (IReadOnlyList<RespValue>)request.Value!;
            var commandName = ((string?)items[0].Value)?.ToUpperInvariant();

            Console.WriteLine($"Command Received: {commandName}");

            if (string.IsNullOrWhiteSpace(commandName))
            {
                await writer.WriteAsync(stream,
                    RespValue.Error("ERR empty command"));
                continue;
            }

            var args = items
                .Skip(1)
                .Select(v => (string?)v.Value ?? string.Empty)
                .ToList();

            if (!CommandRegistry.TryGet(commandName, out var command))
            {
                await writer.WriteAsync(stream,
                    RespValue.Error($"ERR unknown command '{commandName}'"));
                continue;
            }

            var context = new CommandContext
            {
                Client = client,
                Stream = stream,
                Writer = writer
            };

            await command(context, args);

        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Client error: {ex.Message}");
    }
    finally
    {
        client.Close();
    }
}