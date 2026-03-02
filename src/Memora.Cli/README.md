# Memora CLI

<img width="512" height="512" alt="memora_icon (Custom)" src="https://github.com/user-attachments/assets/a4153c53-05f0-4458-9906-f7dfb18d959d" />
 
 Here is a practical plan + starter code to create your own **Memora CLI** — very similar in feel and usage to `redis-cli`, but tailored to your `Memora` server (port 6380 by default, RESP protocol, same commands).

### Goals for the CLI
- Interactive REPL style: type `SET key value`, `GET key`, `KEYS *`, `PING`, etc.
- Supports multi-line / quoted arguments (basic version first)
- Connects to configurable host:port
- Shows errors nicely
- Exit with `exit` or `quit` or Ctrl+C
- Later: history, tab completion, pipe support, scripting mode (`memora-cli -f script.txt`)

### Project Setup
Create a new console project inside your solution:

```bash
dotnet new console -n Memora.Cli
dotnet sln add Memora.Cli/Memora.Cli.csproj
```

Add project reference to your core library (so you can reuse `RespReader` and `RespWriter`):

```bash
cd Memora.Cli
dotnet add reference ../Memora.Core/Memora.Core.csproj
```

(If you don't want to depend on the full core, you can copy `RespReader.cs`, `RespWriter.cs`, `RespValue.cs`, `RespType.cs` into the CLI project — they are small.)

### Minimal Working CLI (start here)

Put this in `Program.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ManuHub.Memora; // ← from Memora.Core (RespReader, RespWriter, RespValue)

namespace Memora.Cli
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string host = "127.0.0.1";
            int port = 6380;

            // Very basic arg parsing (later use System.CommandLine or similar)
            if (args.Length >= 1) host = args[0];
            if (args.Length >= 2 && int.TryParse(args[1], out int p)) port = p;

            Console.WriteLine($"Memora CLI  v0.1  connecting to {host}:{port}");
            Console.WriteLine("Commands: SET, GET, DEL, EXISTS, EXPIRE, TTL, KEYS *, PING, ECHO, FLUSHDB, etc.");
            Console.WriteLine("Type 'exit' or Ctrl+C to quit\n");

            TcpClient? client = null;
            NetworkStream? stream = null;
            RespWriter? writer = null;
            RespReader? reader = null;

            try
            {
                client = new TcpClient();
                await client.ConnectAsync(host, port);
                stream = client.GetStream();
                writer = new RespWriter();
                reader = new RespReader(stream);

                while (true)
                {
                    Console.Write($"{host}:{port}> ");
                    string? input = Console.ReadLine()?.Trim();

                    if (string.IsNullOrWhiteSpace(input))
                        continue;

                    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                        input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                        break;

                    var parts = SplitCommandLine(input);
                    if (parts.Count == 0) continue;

                    string cmd = parts[0].ToUpperInvariant();
                    var cmdArgs = parts.Skip(1).ToList();

                    // Build RESP array: *N \r\n $len\r\n cmd \r\n $len\r\n arg1 \r\n ...
                    var respItems = new List<RespValue> { RespValue.Bulk(cmd) };
                    respItems.AddRange(cmdArgs.Select(RespValue.Bulk));

                    var request = RespValue.Array(respItems.AsReadOnly());

                    await writer.WriteAsync(stream, request);

                    var response = await reader.ReadAsync();
                    PrintResponse(response);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection error: {ex.Message}");
            }
            finally
            {
                stream?.Dispose();
                client?.Dispose();
                Console.WriteLine("Bye.");
            }
        }

        // Naive splitting — doesn't handle quotes yet
        private static List<string> SplitCommandLine(string line)
        {
            return line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .ToList();
        }

        private static void PrintResponse(RespValue? resp)
        {
            if (resp == null)
            {
                Console.WriteLine("(nil)");
                return;
            }

            switch (resp.Type)
            {
                case RespType.SimpleString:
                    Console.WriteLine((string)resp.Value!);
                    break;

                case RespType.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"ERR {(string)resp.Value!}");
                    Console.ResetColor();
                    break;

                case RespType.Integer:
                    Console.WriteLine($"(integer) {(long)resp.Value!}");
                    break;

                case RespType.Bulk:
                    var str = (string?)resp.Value;
                    Console.WriteLine(str ?? "(nil)");
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
                            Console.WriteLine($"{i + 1}) {RespValueToString(items[i])}");
                        }
                    }
                    break;

                case RespType.Null:
                    Console.WriteLine("(nil)");
                    break;

                default:
                    Console.WriteLine($"[unsupported type: {resp.Type}]");
                    break;
            }
        }

        private static string RespValueToString(RespValue v)
        {
            return v.Type switch
            {
                RespType.Bulk => (string?)v.Value ?? "(nil)",
                RespType.SimpleString => (string)v.Value!,
                RespType.Integer => v.Value!.ToString()!,
                RespType.Null => "(nil)",
                _ => $"<{v.Type}>"
            };
        }
    }
}
```

### Quick Improvements (do these next)

1. **Better argument splitting** (handle quoted strings)

   Replace `SplitCommandLine` with something like:

   ```csharp
   private static List<string> SplitCommandLine(string line)
   {
       var result = new List<string>();
       var current = new StringBuilder();
       bool inQuotes = false;

       for (int i = 0; i < line.Length; i++)
       {
           char c = line[i];
           if (c == '"' && (i == 0 || line[i-1] != '\\'))
           {
               inQuotes = !inQuotes;
               continue;
           }
           if (c == ' ' && !inQuotes)
           {
               if (current.Length > 0)
               {
                   result.Add(current.ToString());
                   current.Clear();
               }
           }
           else
           {
               current.Append(c);
           }
       }
       if (current.Length > 0)
           result.Add(current.ToString());

       return result;
   }
   ```

2. **Connection options** (host/port via flags)

   Use `System.CommandLine` NuGet for proper CLI parsing:

   ```bash
   dotnet add package System.CommandLine
   ```

   Then wrap the logic in commands.

3. **History** → add `dotnet add package ReadLine` (very small library)

   ```csharp
   using ReadLine; // after install

   // In loop:
   string? input = ReadLine.Read($"{host}:{port}> ");
   ReadLine.AddHistory(input);
   ```

4. **Pipeline / scripting mode** later

   - `-f file.txt` → read lines from file and execute
   - `-x` → execute one command and exit (good for scripts)

### Testing it right now

Run your server (`Memora.Server` or `Memora.Service`), then:

```bash
dotnet run --project Memora.Cli -- 127.0.0.1 6380
```

Try:

```
PING
SET hello world
GET hello
KEYS *
EXPIRE hello 60
TTL hello
FLUSHDB
```

You should get familiar output.

### Next Level (if you want to go further)

- Colorize output (green OK, red ERR, cyan integers, etc.)
- Show `(integer) 3` style like redis-cli for counts
- Add command help (`HELP SET`)
- Support inline multi-line (like redis-cli --pipe)
- Publish as .NET global tool: `dotnet tool install -g Memora.Cli`

Start with the code above — it should already feel very close to `redis-cli` for your current string-only server.

Let me know what part you'd like to improve first (quoting, history, colors, global tool, etc.) and I can give the exact next snippet.
