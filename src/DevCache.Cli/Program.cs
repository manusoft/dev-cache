using DevCache.Cli.Handlers;
using DevCache.Common;
using System.CommandLine;
using System.Net.Sockets;
using System.Text;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.Title = "DevCache v1.0.0";
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;         

        Option<string> hostOption = new("--host", "-h")
        {
            Description = "Server hostname or IP",
            DefaultValueFactory = _ => "127.0.0.1"
        };

        Option<int> portOption = new("--port", "-p")
        {
            Description = "Server port number",
            DefaultValueFactory = _ => 6380
        };

        Option<string?> urlOption = new("--url")
        {
            Description = "Connection URL in format redis://host:port (overrides host/port)"
        };

        Argument<string[]> commandArgument = new("command")
        {
            Description = "Command and its arguments (for non-interactive/one-shot mode)",
            Arity = ArgumentArity.ZeroOrMore
        };

        var rootCommand = new RootCommand("DevCache CLI – a redis-cli-like client for your DevCache server")
        {
            hostOption,
            portOption,
            urlOption,
            commandArgument
        };


        rootCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            string host = parseResult.GetValue(hostOption) ?? "127.0.0.1";
            int port = parseResult.GetValue(portOption);
            string? url = parseResult.GetValue(urlOption);
            string[] cmdParts = parseResult.GetValue(commandArgument) ?? Array.Empty<string>();

            if (!string.IsNullOrWhiteSpace(url))
            {
                if (!TryParseRedisUrl(url, out var parsedHost, out var parsedPort))
                {
                    Console.Error.WriteLine("Invalid --url format.");
                    return 1;
                }
                host = parsedHost;
                port = parsedPort;
            }

            // One-shot if command provided
            if (cmdParts?.Length > 0)
            {
                try
                {
                    await ExecuteCommand(host, port, cmdParts, ct);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    return 1;
                }
            }

            // Interactive REPL
            await RunInteractiveRepl(host, port, ct);
            return 0;
        });

        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync();
    }

    private static bool TryParseRedisUrl(string? url, out string host, out int port)
    {
        host = "127.0.0.1";
        port = 6380;

        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("redis://", StringComparison.OrdinalIgnoreCase))
            return false;

        var span = url.AsSpan("redis://".Length);
        var colonIndex = span.IndexOf(':');

        if (colonIndex < 0)
        {
            host = span.ToString();
            return true;
        }

        host = span[..colonIndex].ToString();
        return int.TryParse(span[(colonIndex + 1)..], out port);
    }

    private static async Task RunInteractiveRepl(string host, int port, CancellationToken ct)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"DevCache CLI v1.0.0"); // "DevCache CLI v1.0.0 – connected to {host}:{port}"
        Console.WriteLine("Copyright © Manuhub. All rights reserved.");
        Console.WriteLine();
        Console.ResetColor();

        // Optional: seed some helpful commands into history on startup
        ReadLine.AddHistory("HELP");
        ReadLine.AddHistory("KEYS *");
        ReadLine.AddHistory("PING");

        // ── Enable auto-completion ──
        ReadLine.AutoCompletionHandler = new DevCacheAutoCompleteHandler();

        while (!ct.IsCancellationRequested)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            var line = ReadLine.Read($"{host}:{port}> ").Trim();
            Console.ResetColor();
            
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line is "exit" or "quit")
                break;

            if (line is "help" or "--help" or "-h")
            {
                PrintHelp();
                continue;
            }

            if (line == "clearhistory")
            {
                ReadLine.ClearHistory();
                Console.WriteLine("History cleared.");
                continue;
            }

            var parts = SplitCommandLine(line);
            if (parts.Count == 0) continue;

            try
            {
                await ExecuteCommand(host, port, parts, ct);

                if (!string.IsNullOrWhiteSpace(line))
                {
                    ReadLine.AddHistory(line);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }           
        }

        Console.WriteLine("Goodbye.");

        // Optional: clean up (not strictly needed)
        ReadLine.AutoCompletionHandler = null;
    }

    private static async Task ExecuteCommand(string host, int port, IEnumerable<string> parts, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, ct);
        await using var stream = client.GetStream();

        var writer = new RespWriter(stream);
        var reader = new RespReader(stream);

        var cmdList = parts.ToList();
        string cmdName = cmdList[0].ToUpperInvariant();
        var args = cmdList.Skip(1).ToList();

        var respItems = new List<RespValue> { RespValue.BulkString(cmdName) };
        respItems.AddRange(args.Select(RespValue.BulkString));

        await writer.WriteAsync(RespValue.Array(respItems.AsReadOnly()), ct);

        var response = await reader.ReadAsync(ct);
        PrintResponse(response);
    }

    private static void PrintHelp()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Supported commands (based on your current DevCache server):");
        Console.ResetColor();
        Console.WriteLine("  PING [message]");
        Console.WriteLine("  ECHO message");
        Console.WriteLine("  SET key value");
        Console.WriteLine("  GET key");
        Console.WriteLine("  DEL key");
        Console.WriteLine("  EXISTS key");
        Console.WriteLine("  EXPIRE key seconds");
        Console.WriteLine("  TTL key");
        Console.WriteLine("  KEYS *          (only * pattern supported)");
        Console.WriteLine("  FLUSHDB");
        Console.WriteLine("  GETMETA key");
        Console.WriteLine();
        Console.WriteLine("Examples in one-shot mode:");
        Console.WriteLine("  devcache-cli SET counter 100");
        Console.WriteLine("  devcache-cli --url redis://localhost:6380 GET counter");
        Console.WriteLine("  devcache-cli KEYS \"*\"");
    }

    // Basic splitter with quote support (improve later if needed)
    private static List<string> SplitCommandLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (c == ' ' && !inQuotes)
            {
                if (sb.Length > 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
            result.Add(sb.ToString());

        return result;
    }

    private static void PrintResponse(RespValue? resp)
    {
        if (resp is null)
        {
            Console.WriteLine("(nil)");
            return;
        }

        switch (resp.Type)
        {
            case RespType.SimpleString:
                Console.WriteLine(resp.Value);
                break;

            case RespType.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                string errMsg = (string)resp.Value!;
                // Remove duplicate "ERR " if server already includes it
                if (errMsg.StartsWith("ERR ", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine(errMsg);
                else
                    Console.WriteLine($"ERR {errMsg}");
                Console.ResetColor();
                break;

            case RespType.Integer:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"(integer) {resp.Value}");
                Console.ResetColor();
                break;

            case RespType.BulkString:
                Console.WriteLine(resp.Value ?? "(nil)");
                break;

            case RespType.Array:
                var items = (IReadOnlyList<RespValue>)resp.Value!;
                if (items.Count == 0)
                {
                    Console.WriteLine("(empty list or set)");
                }
                else
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        Console.WriteLine($"  {i + 1}) {RespValueToString(items[i])}");
                    }
                }
                break;

            case RespType.NullBulk:
                Console.WriteLine("(nil)");
                break;

            default:
                Console.WriteLine($"[unsupported: {resp.Type}]");
                break;
        }
    }

    private static string RespValueToString(RespValue v)
    {
        return v.Type switch
        {
            RespType.BulkString => v.Value?.ToString() ?? "(nil)",
            RespType.SimpleString => v.Value?.ToString() ?? "",
            RespType.Integer => v.Value?.ToString() ?? "0",
            _ => $"<{v.Type}>"
        };
    }
}