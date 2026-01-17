using DevCache;
using System.Net;
using System.Net.Sockets;

bool runInBackground = args.Contains("--background");

if (!runInBackground)
    Console.WriteLine("DevCache starting on port 6380...");

var listener = new TcpListener(IPAddress.Parse("127.0.0.1"), 6380);
listener.Start();

if (!runInBackground)
    Console.WriteLine("DevCache listening on 127.0.0.1:6380");

if (runInBackground)
{
    var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devcache.log");
    File.AppendAllText(logFile, $"{DateTime.Now}: Server started\n");
}


while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = HandleClientAsync(client, runInBackground);
}

static async Task HandleClientAsync(TcpClient client, bool runInBackground)
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
                if (!runInBackground)
                    Console.WriteLine("Protocol error: expected array");

                await writer.WriteAsync(stream,
                    RespValue.Error("Protocol error: expected array"));
                continue;
            }

            var items = (IReadOnlyList<RespValue>)request.Value!;
            var commandName = ((string?)items[0].Value)?.ToUpperInvariant();

            if (!runInBackground)
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
        if (!runInBackground)
            Console.WriteLine($"Client error: {ex.Message}");
    }
    finally
    {
        client.Close();
    }
}