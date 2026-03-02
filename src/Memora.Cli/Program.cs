using ManuHub.Memora;
using ManuHub.Memora.Common;
using ManuHub.Memora.Handlers;
using System.CommandLine;
using System.Net.Sockets;
using System.Text;

internal class Program
{
    private static TcpClient? _client;
    private static NetworkStream? _stream;
    private static RespReader? _reader;
    private static RespWriter? _writer;

    private static string _host = "127.0.0.1";
    private static int _port = 6380;

    private static bool IsConnected => _client?.Connected == true;

    private static async Task<int> Main(string[] args)
    {
        Console.Title = "Memora CLI v1.0.0";
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        Option<string> hostOption = new("--host", "-h") { Description = "Server hostname or IP", DefaultValueFactory = _ => "127.0.0.1" };
        Option<int> portOption = new("--port", "-p") { Description = "Server port number", DefaultValueFactory = _ => 6380 };
        Option<string?> urlOption = new("--url") { Description = "Connection URL in format redis://host:port" };
        Argument<string[]> commandArgument = new("command") { Description = "Command and its arguments (for non-interactive mode)", Arity = ArgumentArity.ZeroOrMore };

        var rootCommand = new RootCommand("Memora CLI – redis-cli-like client for Memora")
        {
            hostOption, portOption, urlOption, commandArgument
        };

        rootCommand.SetAction(async (parseResult, ct) =>
        {
            string host = parseResult.GetValue(hostOption) ?? "127.0.0.1";
            int port = parseResult.GetValue(portOption);
            string? url = parseResult.GetValue(urlOption);
            string[] cmdParts = parseResult.GetValue(commandArgument) ?? Array.Empty<string>();

            if (!string.IsNullOrWhiteSpace(url))
            {
                if (!TryParseRedisUrl(url, out var parsedHost, out var parsedPort))
                {
                    Console.Error.WriteLine("Invalid --url format. Use redis://host:port");
                    return 1;
                }
                host = parsedHost;
                port = parsedPort;
            }

            _host = host;
            _port = port;

            // One-shot mode
            if (cmdParts.Length > 0)
            {
                try
                {
                    await ConnectIfNeeded(ct);
                    await ExecuteCommand(cmdParts, ct);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    return 1;
                }
                finally
                {
                    Disconnect();
                }
            }

            // Interactive REPL
            await RunInteractiveRepl(ct);
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

    private static async Task RunInteractiveRepl(CancellationToken ct)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("Memora CLI v1.0.0");
        Console.WriteLine($"Copyright © {DateTime.UtcNow.Year} Manuhub. All rights reserved.");
        Console.ResetColor();

        // Try to auto-connect at startup
        bool connectedOnStartup = await TryAutoConnectOnStartup(ct);

        Console.WriteLine();

        if (connectedOnStartup)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Type HELP for commands, EXIT to quit.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Not connected. Use CONNECT <{_host}> <{_port}> to connect.");
            Console.ResetColor();
        }

        Console.WriteLine();

        // History & auto - complete
        ReadLine.AddHistory("HELP");
        ReadLine.AddHistory("KEYS *");
        ReadLine.AddHistory("PING");
        ReadLine.AutoCompletionHandler = new MemoraAutoCompleteHandler();

        while (!ct.IsCancellationRequested)
        {
            string status = IsConnected ? "memora-cli" : $"[Not connected] {_host}:{_port}";
            Console.ForegroundColor = IsConnected ? ConsoleColor.Green : ConsoleColor.Red;
            var line = ReadLine.Read($"{status}> ")?.Trim();
            Console.ResetColor();

            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = SplitCommandLine(line);
            if (parts.Count == 0) continue;

            string cmd = parts[0].ToUpperInvariant();

            // Local CLI commands
            if (cmd == "CONNECT" || cmd == "CONN")
            {
                await TryConnect(parts);
                continue;
            }
            else if (cmd == "DISCONNECT" || cmd == "DISC")
            {
                Disconnect();
                Console.WriteLine("Disconnected.");
                continue;
            }
            else if (cmd == "CLEAR" || cmd == "CLS" || cmd == "CLEARSCREEN")
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.ResetColor();
                continue;
            }
            else if (cmd == "CLEARHISTORY" || cmd == "CLRHIST")
            {
                ReadLine.ClearHistory();
                Console.WriteLine("Command history cleared.");
                continue;
            }
            else if (cmd == "HELP" || cmd == "--HELP" || cmd == "-H")
            {
                PrintHelp();
                continue;
            }
            else if (cmd == "EXIT" || cmd == "QUIT")
            {
                break;
            }

            // Server command
            if (!IsConnected)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: Not connected. Use CONNECT <host> <port> to connect.");
                Console.ResetColor();
                continue;
            }

            try
            {
                await ExecuteCommand(parts, ct);
                if (!string.IsNullOrWhiteSpace(line))
                    ReadLine.AddHistory(line);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
                Disconnect(); // auto-disconnect on failure
            }
        }

        Disconnect();
        Console.WriteLine("Goodbye.");
        ReadLine.AutoCompletionHandler = null;
    }

    private static async Task<bool> TryAutoConnectOnStartup(CancellationToken ct)
    {
        try
        {
            await ConnectIfNeeded(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task TryConnect(IReadOnlyList<string> args)
    {
        string host = _host;
        int port = _port;

        if (args.Count >= 2 && !string.IsNullOrWhiteSpace(args[1]))
            host = args[1];

        if (args.Count >= 3 && int.TryParse(args[2], out int p))
            port = p;

        Disconnect();

        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(host, port);
            _stream = _client.GetStream();
            _reader = new RespReader(_stream);
            _writer = new RespWriter(_stream);

            _host = host;
            _port = port;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Connected to {host}:{port}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Connection failed: {ex.Message}");
            Console.ResetColor();
            Disconnect();
        }
    }

    private static void Disconnect()
    {
        _reader = null;
        _writer = null;
        _stream?.Dispose();
        _stream = null;
        _client?.Close();
        _client?.Dispose();
        _client = null;
    }

    private static async Task ConnectIfNeeded(CancellationToken ct)
    {
        if (IsConnected) return;

        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port, ct);
            _stream = _client.GetStream();
            _reader = new RespReader(_stream);
            _writer = new RespWriter(_stream);
        }
        catch
        {
            Disconnect();
            throw;
        }
    }

    private static async Task ExecuteCommand(IReadOnlyList<string> parts, CancellationToken ct)
    {
        if (_writer == null || _reader == null || _stream == null)
            throw new InvalidOperationException("Not connected");

        string cmd = parts[0].ToUpperInvariant();

        // Special confirmation for dangerous commands
        if (cmd == "FLUSHDB" || cmd == "FLUSHALL")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"WARNING: {cmd} will delete ALL keys in the database!");
            Console.Write("Are you sure? Type 'YES' to confirm: ");
            Console.ResetColor();

            string? confirmation = Console.ReadLine()?.Trim().ToUpperInvariant();

            if (confirmation != "YES")
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Operation cancelled.");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Confirmation received. Executing...");
            Console.ResetColor();
        }

        // Build RESP request
        var respItems = new List<RespValue>();

        respItems.Add(RespValue.BulkString(parts[0])); // command

        for (int i = 1; i < parts.Count; i++)
        {
            string val = parts[i];
            respItems.Add(RespValue.BulkString(val));
        }

        var request = RespValue.Array(respItems.AsReadOnly());

        await _writer.WriteAsync(request, ct);

        var response = await _reader.ReadAsync(ct);
        if (response == null)
        {
            Disconnect();
            throw new Exception("Server disconnected");
        }

        PrintResponse(response);
    }

    private static void PrintHelp()
    {
        const string indent = "  ";
        const string subIndent = "    ";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Memora CLI v1.0.0 – Supported Commands");
        Console.WriteLine("=========================================");
        Console.ResetColor();

        // ────────────────────────────────────────────────
        // Local CLI commands (handled by client)
        // ────────────────────────────────────────────────
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Local CLI commands (work even when not connected):");
        Console.ResetColor();

        Console.WriteLine($"{indent}CONNECT [host] [port]         Connect to Memora server (default: 127.0.0.1:6380)");
        Console.WriteLine($"{subIndent}Aliases: CONN");
        Console.WriteLine($"{indent}DISCONNECT                    Disconnect current connection");
        Console.WriteLine($"{subIndent}Aliases: DISC");
        Console.WriteLine($"{indent}CLEAR / CLS / CLEARSCREEN     Clear the console screen");
        Console.WriteLine($"{indent}CLEARHISTORY / CLRHIST        Clear command history");
        Console.WriteLine($"{indent}HELP / --help / -h            Show this help message");
        Console.WriteLine($"{indent}EXIT / QUIT                   Exit the CLI");

        Console.WriteLine();

        // ────────────────────────────────────────────────
        // Server commands (sent to Memora when connected)
        // ────────────────────────────────────────────────
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Server commands (only when connected):");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{indent}Core / Diagnostic:");
        Console.ResetColor();
        Console.WriteLine($"{subIndent}PING [message]              Returns PONG or echoes message");
        Console.WriteLine($"{subIndent}ECHO message                Returns the message");
        Console.WriteLine($"{subIndent}INFO [section]              Show server information (all, memory, keyspace, stats, etc.)");
        Console.WriteLine($"{subIndent}CONFIG GET <param> [...]    Get configuration parameters");
        Console.WriteLine($"{subIndent}ROLE                        Show replication role (master/replica/sentinel)");
        Console.WriteLine($"{subIndent}CLIENT LIST                 List connected clients (basic support)");

        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{indent}Key-Value (Strings):");
        Console.ResetColor();
        Console.WriteLine($"{subIndent}SET key value [EX sec]      Set key with optional expiry");
        Console.WriteLine($"{subIndent}GET key                     Get value");
        Console.WriteLine($"{subIndent}DEL key                     Delete key");
        Console.WriteLine($"{subIndent}EXISTS key                  Check if key exists (1/0)");
        Console.WriteLine($"{subIndent}INCR key                    Increment integer by 1");
        Console.WriteLine($"{subIndent}DECR key                    Decrement integer by 1");
        Console.WriteLine($"{subIndent}INCRBY key amount           Increment integer by amount");

        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{indent}TTL / Expiry:");
        Console.ResetColor();
        Console.WriteLine($"{subIndent}EXPIRE key seconds          Set expiry in seconds");
        Console.WriteLine($"{subIndent}PEXPIRE key milliseconds    Set expiry in milliseconds");
        Console.WriteLine($"{subIndent}TTL key                     Remaining TTL in seconds (-1=no expiry, -2=not found)");
        Console.WriteLine($"{subIndent}PTTL key                    Remaining TTL in milliseconds");

        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{indent}Database:");
        Console.ResetColor();
        Console.WriteLine($"{subIndent}FLUSHDB                     Delete all keys in current database");

        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{indent}Introspection / UI:");
        Console.ResetColor();
        Console.WriteLine($"{subIndent}KEYS pattern                List keys (only * pattern supported)");
        Console.WriteLine($"{subIndent}TYPE key                    Return type of key (string/list/hash/etc.)");
        Console.WriteLine($"{subIndent}GETMETA key                 Get metadata (type, TTL, size)");

        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{indent}Lists:");
        Console.ResetColor();
        Console.WriteLine($"{subIndent}LPUSH key value [value...]  Push values to head of list");
        Console.WriteLine($"{subIndent}RPUSH key value [value...]  Push values to tail of list");
        Console.WriteLine($"{subIndent}LPOP key                    Pop value from head");
        Console.WriteLine($"{subIndent}RPOP key                    Pop value from tail");
        Console.WriteLine($"{subIndent}LLEN key                    Get list length");
        Console.WriteLine($"{subIndent}LRANGE key start stop       Get range of list items");

        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{indent}Hashes:");
        Console.ResetColor();
        Console.WriteLine($"{subIndent}HSET key field value        Set field in hash");
        Console.WriteLine($"{subIndent}HGET key field              Get field from hash");
        Console.WriteLine($"{subIndent}HDEL key field              Delete field from hash");
        Console.WriteLine($"{subIndent}HLEN key                    Get number of fields in hash");
        Console.WriteLine($"{subIndent}HKEYS key                   Get all field names");
        Console.WriteLine($"{subIndent}HVALS key                   Get all field values");

        Console.WriteLine();

        // One-shot examples
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("One-shot mode examples:");
        Console.ResetColor();
        Console.WriteLine($"{indent}memora-cli SET counter 100");
        Console.WriteLine($"{indent}memora-cli --url redis://localhost:6380 GET counter");
        Console.WriteLine($"{indent}memora-cli KEYS \"*\"");
        Console.WriteLine($"{indent}memora-cli INCRBY visits 1");

        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Note: Type any command directly when connected — more may be supported by the server.");
        Console.ResetColor();
    }

    // Basic splitter with quote support (improve later if needed)
    private static List<string> SplitCommandLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool escaped = false;

        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];

            if (escaped)
            {
                current.Append(c);
                escaped = false;
                i++;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                i++;
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                i++;

                // If we just closed quotes...
                if (!inQuotes)
                {
                    // Add whatever is in current (even empty)
                    string token = current.ToString();
                    result.Add(token);
                    current.Clear();
                }
                continue;
            }

            if (c == ' ' && !inQuotes)
            {
                // Add current token if any
                if (current.Length > 0)
                {
                    string token = current.ToString();
                    result.Add(token);
                    current.Clear();
                }
                // Skip extra spaces
                while (i < line.Length && line[i] == ' ')
                    i++;
                continue;
            }

            current.Append(c);
            i++;
        }

        // Add last token (could be empty if ended with quotes)
        if (current.Length > 0 || (line.EndsWith("\"") && inQuotes == false))
        {
            string last = current.ToString();
            result.Add(last);
        }

        // Remove only completely whitespace tokens (keep "")
        result.RemoveAll(s => string.IsNullOrWhiteSpace(s) && s != "");

        return result;
    }

    private static void PrintResponse(RespValue? resp, int indentLevel = 0)
    {
        string indent = new string(' ', indentLevel * 3);

        if (resp is null)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("(nil)");
            Console.ResetColor();
            return;
        }

        switch (resp.Type)
        {
            case RespType.SimpleString:
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(resp.Value is null ? $"\"\"" : $"\"{resp.Value}\"");
                Console.ResetColor();
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
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"(integer) {resp.Value}");
                Console.ResetColor();
                break;

            case RespType.BulkString:
                string display = resp.Value switch
                {
                    null => "(nil)",
                    "" => "(empty string)",
                    _ => $"\"{resp.Value}\""
                };
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(display);
                Console.ResetColor();
                break;

            case RespType.Array:
                var items = (IReadOnlyList<RespValue>)resp.Value!;

                // Special handling for SCAN replies (cursor + array of keys)
                if (items.Count == 2 && items[0].Type == RespType.BulkString && items[1].Type == RespType.Array)
                {
                    string cursor = items[0].AsString() ?? "0";
                    var keys = items[1].AsArray() ?? Array.Empty<RespValue>();

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"  1. \"{cursor}\"");

                    if (keys.Count == 0)
                    {
                        Console.WriteLine("  2. (empty array)");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine($"  2.");

                        for (int i = 0; i < keys.Count; i++)
                        {
                            string keyStr = keys[i].AsString() ?? "(nil)";
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"     {i + 1}) \"{keyStr}\"");
                            Console.ResetColor();
                        }
                    }
                }
                // Normal array (non-SCAN)
                else if (items.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine("(empty list or set)");
                    Console.ResetColor();
                }
                else
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine($"  {i + 1}) {RespValueToString(items[i])}");
                        Console.ResetColor();
                    }
                }
                break;

            case RespType.NullBulk:
            case RespType.NullArray:
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("(nil)");
                Console.ResetColor();
                break;

            default:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[unsupported: {resp.Type}]");
                Console.ResetColor();
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

    // Optional: add this for better disconnect detection in REPL
    private static async Task<bool> IsServerAlive()
    {
        if (!IsConnected) return false;
        try
        {
            await ExecuteCommand(new[] { "PING" }, CancellationToken.None);
            return true;
        }
        catch
        {
            Disconnect();
            return false;
        }
    }
}