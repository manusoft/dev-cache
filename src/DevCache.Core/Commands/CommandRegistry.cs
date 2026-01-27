using DevCache.Common;
using DevCache.Core.Helpers;
using DevCache.Core.Models;
using DevCache.Core.Storage;
using System.Net;

namespace DevCache.Core.Commands;

public static class CommandRegistry
{
    private static readonly Dictionary<string, RedisCommand> _commands;
    private static InMemoryStore? _store;
    private static ServerRuntimeInfo? _runtimeInfo;
    private static string? _requirePass;

    public static InMemoryStore Store => _store
        ?? throw new InvalidOperationException("Store not initialized");

    public static bool TryGet(string name, out RedisCommand command)
        => _commands.TryGetValue(name, out command!);
        
    public static string? RequirePass => _requirePass;

    static CommandRegistry()
    {
        _commands = new(StringComparer.OrdinalIgnoreCase)
        {
            // Core
            ["AUTH"] = AuthAsync,
            ["CONFIG"] = ConfigAsync,
            ["CLIENT"] = ClientAsync,           
            ["ECHO"] = EchoAsync,
            ["INFO"] = InfoAsync,
            ["PING"] = PingAsync,
            ["ROLE"] = RoleAsync,            
            ["QUIT"] = QuitAsync,            

            // KV (Strings)
            ["SET"] = SetAsync,
            ["GET"] = GetAsync,
            ["DEL"] = DelAsync,
            ["EXISTS"] = ExistsAsync,
            ["INCR"] = IncrAsync,
            ["DECR"] = DecrAsync,
            ["INCRBY"] = IncrByAsync,
            ["SCAN"] = ScanAsync,
            ["GETRANGE"] = GetRangeAsync,

            // TTL
            ["EXPIRE"] = ExpireAsync,
            ["TTL"] = TtlAsync,
            ["PEXPIRE"] = PExpireAsync,
            ["PTTL"] = PTtlAsync,

            // DB
            ["FLUSHDB"] = FlushDbAsync,
            ["FLUSHALL"] = FlushAllAsync,

            // UI / Introspection
            ["KEYS"] = KeysAsync,
            ["TYPE"] = TypeAsync,
            ["GETMETA"] = GetMetaAsync,

            // Lists
            ["LPUSH"] = LPushAsync,
            ["RPUSH"] = RPushAsync,
            ["LPOP"] = LPopAsync,
            ["RPOP"] = RPopAsync,
            ["LLEN"] = LLenAsync,
            ["LRANGE"] = LRangeAsync,

            // Hashes
            ["HSET"] = HSetAsync,
            ["HGET"] = HGetAsync,
            ["HDEL"] = HDelAsync,
            ["HLEN"] = HLenAsync,
            ["HKEYS"] = HKeysAsync,
            ["HVALS"] = HValsAsync,
            ["HSCAN"] = HScanAsync,
            ["HGETALL"] = HGetAllAsync,

        };
    }

    public static void Initialize(InMemoryStore store, ServerRuntimeInfo runtimeInfo, string? requirePass = null)
    {
        if (_store != null) throw new InvalidOperationException("Already initialized");
        _store = store;
        _runtimeInfo = runtimeInfo;

        // Normalize: treat empty/whitespace as "no password"
        _requirePass = string.IsNullOrWhiteSpace(requirePass) ? null : requirePass;       
    }

    // ---------------- Core ----------------

    private static async Task AuthAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error("ERR wrong number of arguments for 'auth' command"));
            return;
        }

        string provided = args[0];

        // If no password is required → always accept
        if (RequirePass == null || string.IsNullOrEmpty(RequirePass))
        {
            ctx.IsAuthenticated = true;
            await ctx.Writer.WriteAsync(RespValue.SimpleString("OK"));
            return;
        }

        // Real check (simple string comparison is fine for now)
        if (provided == RequirePass)
        {
            ctx.IsAuthenticated = true;
            await ctx.Writer.WriteAsync(RespValue.SimpleString("OK"));
        }
        else
        {
            ctx.IsAuthenticated = false;
            await ctx.Writer.WriteAsync(
                RespValue.Error("ERR invalid password"));
        }
    }

    private static async Task ClientAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error("ERR wrong number of arguments for 'client' command")
            );
            return;
        }

        string subcmd = args[0].ToUpperInvariant();

        if (subcmd == "LIST")
        {
            // Very basic fake output - pretend there is one active client
            // Format matches real Redis client list line
            var now = DateTimeOffset.UtcNow;
            long ageSeconds = (long)(now - ctx.ConnectedAt).TotalSeconds;

            string clientInfo =
                $"id=1 " +
                $"addr={ctx.Client.Client.RemoteEndPoint?.ToString() ?? "unknown:0"} " +
                $"fd=8 " +                           // fake file descriptor
                $"name= " +                          // client name (empty if not set)
                $"age={ageSeconds} " +
                $"idle=0 " +                         // idle time in seconds
                $"flags=N " +                        // N = normal client
                $"db=0 " +
                $"sub=0 " +
                $"psub=0 " +
                $"qbuf=0 " +
                $"qbuf-free=0 " +
                $"obl=0 " +
                $"oll=0 " +
                $"omem=0 " +
                $"events=r " +                       // r = readable
                $"cmd=client " +                     // last command
                $"user=default " +
                $"redir=-1";

            await ctx.Writer.WriteAsync(
                RespValue.BulkString(clientInfo + "\n")
            );
        }
        else if (subcmd == "GETNAME" || subcmd == "SETNAME")
        {
            await ctx.Writer.WriteAsync(RespValue.Error("ERR CLIENT GETNAME/SETNAME not supported yet"));
        }
        else
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error($"ERR Unsupported CLIENT subcommand or wrong number of arguments for 'CLIENT|client {subcmd}'")
            );
        }
    }

    private static async Task ConfigAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error("ERR wrong number of arguments for 'config' command")
            );
            return;
        }

        string subcmd = args[0].ToUpperInvariant();

        if (subcmd == "GET")
        {
            if (args.Count < 2)
            {
                await ctx.Writer.WriteAsync(RespValue.Error("ERR wrong number of arguments for 'config get'"));
                return;
            }

            var result = new List<RespValue>();

            // Special case: CONFIG GET *
            if (args.Count == 2 && args[1] == "*")
            {
                var allKeys = new[]
                {
                    "port", "bind", "maxmemory", "maxmemory-policy", "timeout",
                    "databases", "appendonly", "aof-enabled", "requirepass",
                    "protected-mode", "tcp-keepalive", "hz"
                };

                foreach (var k in allKeys)
                {
                    string? val = GetConfigValue(k);
                    result.Add(RespValue.BulkString(k));
                    result.Add(RespValue.BulkString(val ?? "(nil)"));
                }
            }
            else
            {
                // Normal case: one or more explicit keys
                for (int i = 1; i < args.Count; i++)
                {
                    string key = args[i].ToLowerInvariant();
                    string? val = GetConfigValue(key);

                    result.Add(RespValue.BulkString(key));
                    result.Add(RespValue.BulkString(val ?? "(nil)"));
                }
            }

            await ctx.Writer.WriteAsync(RespValue.Array(result.AsReadOnly()));
        }
        else if (subcmd == "SET")
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error("ERR CONFIG SET is not supported yet")
            );
        }
        else if (subcmd == "RESETSTAT")
        {
            // Optional – reset some counters
            await ctx.Writer.WriteAsync(RespValue.SimpleString("OK"));
        }
        else
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error($"ERR Unknown subcommand or wrong number of arguments for 'CONFIG|config {subcmd}'")
            );
        }
    }

    private static async Task EchoAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'echo' command");
            return;
        }

        await ctx.Writer.WriteAsync(RespValue.BulkString(args[0]));
    }

    private static async Task InfoAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        string section;

        switch (args.Count)
        {
            case 0:
                section = "all";
                break;

            case 1:
                section = args[0].ToLowerInvariant();
                break;

            default:
                await ctx.Writer.WriteAsync(
                    RespValue.Error("ERR wrong number of arguments for 'info' command")
                );
                return;
        }

        var handler = new InfoCommandHandler(CommandRegistry.Store, CommandRegistry._runtimeInfo!);
        string result = handler.Execute(new[] { section });

        await ctx.Writer.WriteAsync(RespValue.BulkString(result));
    }

    private static async Task PingAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count > 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'ping' command");
            return;
        }

        string response = args.Count == 1 ? args[0] : "PONG";
        await ctx.Writer.WriteAsync(RespValue.SimpleString(response));
    }

    private static async Task RoleAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 0)
        {
            await ctx.Writer.WriteAsync(RespValue.Error("ERR wrong number of arguments for 'role' command"));
            return;
        }

        // Build the reply array:
        // 1) role as simple string
        // 2) replication offset (integer) — fake 0 for now
        // 3) list of connected replicas — empty array for now
        var reply = new List<RespValue>
        {
            RespValue.SimpleString("master"),
            RespValue.Integer(0),                              // master_repl_offset
            RespValue.Array(Array.Empty<RespValue>()),         // connected replicas
            RespValue.Integer(0)                               // optional: second_repl_offset (usually 0)
        };

        await ctx.Writer.WriteAsync(RespValue.Array(reply.AsReadOnly()));
    }
    
    private static async Task QuitAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count > 0)
        {
            await ctx.Writer.WriteAsync(RespValue.Error("ERR wrong number of arguments for 'quit' command"));
            return;
        }

        await ctx.Writer.WriteAsync(RespValue.SimpleString("OK"));
        // The connection will close naturally after this reply because we break the loop on client side
    }

    // ---------------- KV (Strings) ----------------
    private static async Task SetAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            await Error(ctx, "ERR wrong number of arguments for 'set' command");
            return;
        }

        string key = args[0];
        string value = args[1];

        bool? nx = null;

        long? expireMs = null;

        for (int i = 2; i < args.Count; i++)
        {
            string opt = args[i].ToUpperInvariant();

            switch (opt)
            {
                case "EX":
                    if (i + 1 >= args.Count || !int.TryParse(args[i + 1], out int sec) || sec <= 0)
                    {
                        await Error(ctx, "ERR value is not an integer or out of range");
                        return;
                    }
                    expireMs = sec * 1000L;
                    i++;
                    break;

                case "PX":
                    if (i + 1 >= args.Count || !long.TryParse(args[i + 1], out long ms) || ms <= 0)
                    {
                        await Error(ctx, "ERR value is not an integer or out of range");
                        return;
                    }
                    expireMs = ms;
                    i++;
                    break;

                case "NX":
                    nx = true;
                    break;

                case "XX":
                    nx = false;
                    break;

                default:
                    await Error(ctx, $"ERR syntax error near '{opt}'");
                    return;
            }
        }

        bool keyExists = Store.Exists(key);

        if (nx == true && keyExists)
        {
            await ctx.Writer.WriteAsync(RespValue.NullBulk);
            return;
        }

        if (nx == false && !keyExists)
        {
            await ctx.Writer.WriteAsync(RespValue.NullBulk);
            return;
        }

        // Set the value (overwrites any type with string)
        Store.Set(key, value, persist: true);

        // Apply expiry AFTER set
        if (expireMs.HasValue)
        {
            Store.Expire(key, expireMs.Value, persist: true);
        }

        await ctx.Writer.WriteAsync(RespValue.SimpleString("OK"));
    }

    private static async Task GetAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'get' command");
            return;
        }

        var entry = Store.GetEntry(args[0]);
        if (entry is not StringEntry strEntry)
        {
            await Error(ctx, entry == null ? "ERR no such key" : "WRONGTYPE Operation against a key holding the wrong kind of value");
            return;
        }

        await ctx.Writer.WriteAsync(RespValue.BulkString(strEntry.Value));
    }

    private static async Task DelAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'del' command");
            return;
        }

        var response = Store.Del(args[0]) ? 1 : 0;
        await ctx.Writer.WriteAsync(RespValue.Integer(response));
    }

    private static async Task ExistsAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'exists' command");
            return;
        }

        var response = Store.Exists(args[0]) ? 1 : 0;
        await ctx.Writer.WriteAsync(RespValue.Integer(response));
    }

    private static async Task IncrAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error("ERR wrong number of arguments for 'incr' command")
            );
            return;
        }

        string key = args[0];
        long newValue = CommandRegistry.Store.Incr(key, 1);

        await ctx.Writer.WriteAsync(RespValue.Integer(newValue));
    }

    private static async Task DecrAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error("ERR wrong number of arguments for 'decr' command")
            );
            return;
        }

        string key = args[0];
        long newValue = CommandRegistry.Store.Incr(key, -1);

        await ctx.Writer.WriteAsync(RespValue.Integer(newValue));
    }

    private static async Task IncrByAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 2)
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error("ERR wrong number of arguments for 'incrby' command")
            );
            return;
        }

        string key = args[0];
        if (!long.TryParse(args[1], out long increment))
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error("ERR value is not an integer or out of range")
            );
            return;
        }

        long newValue = CommandRegistry.Store.Incr(key, increment);

        await ctx.Writer.WriteAsync(RespValue.Integer(newValue));
    }

    private static async Task ScanAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count < 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'scan' command");
            return;
        }

        // Parse cursor (first arg) — must be non-negative integer
        if (!ulong.TryParse(args[0], out ulong cursor))
        {
            await Error(ctx, "ERR invalid cursor");
            return;
        }

        string? matchPattern = null;
        int countHint = 10; // default ~10 keys per iteration

        // Parse optional MATCH and COUNT
        for (int i = 1; i < args.Count; i++)
        {
            string opt = args[i].ToUpperInvariant();
            if (opt == "MATCH" && i + 1 < args.Count)
            {
                matchPattern = args[++i];
            }
            else if (opt == "COUNT" && i + 1 < args.Count)
            {
                if (!int.TryParse(args[++i], out int cnt) || cnt <= 0)
                {
                    await Error(ctx, "ERR invalid COUNT value");
                    return;
                }
                countHint = cnt;
            }
            else
            {
                await Error(ctx, $"ERR unknown option '{opt}' or wrong number of arguments");
                return;
            }
        }

        // Get all non-expired keys (your existing Store.Keys)
        var allKeys = CommandRegistry.Store.Keys.ToList(); // Materialize to list for indexing

        // If cursor is at end (0), start from beginning
        if (cursor == 0)
        {
            cursor = 0; // reset
        }
        else if (cursor >= (ulong)allKeys.Count)
        {
            // Invalid cursor: too large → return end immediately
            await ReplyWithCursorAndKeys(ctx, 0, new List<RespValue>());
            return;
        }

        // Calculate scan range (approximate countHint)
        int startIndex = (int)cursor;
        int endIndex = Math.Min(startIndex + countHint, allKeys.Count);

        var scannedKeys = new List<RespValue>();
        for (int j = startIndex; j < endIndex; j++)
        {
            string key = allKeys[j];
            // Apply MATCH filter if specified
            if (matchPattern == null || CommonHelper.MatchesPattern(key, matchPattern))
            {
                scannedKeys.Add(RespValue.BulkString(key));
            }
        }

        // Next cursor: position after last scanned key (or 0 if end)
        ulong nextCursor = (ulong)(endIndex < allKeys.Count ? endIndex : 0);

        await ReplyWithCursorAndKeys(ctx, nextCursor, scannedKeys);
    }

    private static async Task GetRangeAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 3)
        {
            await Error(ctx, "ERR wrong number of arguments for 'getrange' command");
            return;
        }

        string key = args[0];
        string sStart = args[1];
        string sEnd = args[2];

        // Parse start & end
        if (!long.TryParse(sStart, out long start) || !long.TryParse(sEnd, out long end))
        {
            await Error(ctx, "ERR value is not an integer or out of range");
            return;
        }

        var entry = Store.GetEntry(key);
        if (entry == null)
        {
            // non-existing key → empty string (Redis behavior)
            await ctx.Writer.WriteAsync(RespValue.BulkString(""));
            return;
        }

        if (entry is not StringEntry strEntry)
        {
            await Error(ctx, "WRONGTYPE Operation against a key holding the wrong kind of value");
            return;
        }

        string value = strEntry.Value;
        if (value.Length == 0)
        {
            await ctx.Writer.WriteAsync(RespValue.BulkString(""));
            return;
        }

        // Handle negative offsets
        if (start < 0) start = value.Length + start;
        if (end < 0) end = value.Length + end;

        // Clamp to valid range
        start = Math.Max(0, start);
        end = Math.Min(value.Length - 1, end);

        if (start > end)
        {
            await ctx.Writer.WriteAsync(RespValue.BulkString(""));
            return;
        }

        string result = value.Substring((int)start, (int)(end - start + 1));

        await ctx.Writer.WriteAsync(RespValue.BulkString(result));
    }

    // ---------------- TTL ----------------
    private static async Task ExpireAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 2)
        {
            await Error(ctx, "ERR wrong number of arguments for 'expire' command");
            return;
        }

        if (!int.TryParse(args[1], out var seconds) || seconds < 0)
        {
            await Error(ctx, "ERR invalid expire time");
            return;
        }

        var response = Store.Expire(args[0], seconds * 1000L) ? 1 : 0;
        await ctx.Writer.WriteAsync(RespValue.Integer(response));
    }

    private static async Task TtlAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'ttl' command");
            return;
        }

        await ctx.Writer.WriteAsync(RespValue.Integer(Store.GetTtlSeconds(args[0])));
    }

    private static async Task PExpireAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 2)
        {
            await Error(ctx, "ERR wrong number of arguments for 'pexpire' command");
            return;
        }

        if (!long.TryParse(args[1], out long ms) || ms < 0)
        {
            await Error(ctx, "ERR value is not an integer or out of range");
            return;
        }

        var response = Store.Expire(args[0], ms) ? 1 : 0;
        await ctx.Writer.WriteAsync(RespValue.Integer(response));
    }

    private static async Task PTtlAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'pttl' command");
            return;
        }

        await ctx.Writer.WriteAsync(RespValue.Integer(Store.GetTtlMilliseconds(args[0])));
    }

    // ---------------- DB ----------------  
    private static async Task FlushDbAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 0)
        {
            await ctx.Writer.WriteAsync(RespValue.Error("ERR wrong number of arguments for 'flushdb' command"));
            return;
        }

        CommandRegistry.Store.FlushDb(persist: true); // live → persist
        await ctx.Writer.WriteAsync(RespValue.SimpleString("OK"));
    }

    private static async Task FlushAllAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 0)
        {
            await ctx.Writer.WriteAsync(RespValue.Error("ERR wrong number of arguments for 'flushall' command"));
            return;
        }

        CommandRegistry.Store.FlushAll(persist: true); // live → persist
        await ctx.Writer.WriteAsync(RespValue.SimpleString("OK"));
    }

    // ---------------- UI / Introspection ----------------
    private static async Task KeysAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        string pattern = "*"; // default

        if (args.Count == 0)
        {
            pattern = "*";
        }
        else if (args.Count == 1)
        {
            pattern = args[0];
        }
        else
        {
            await ctx.Writer.WriteAsync(RespValue.Error("ERR wrong number of arguments for 'keys' command"));
            return;
        }

        // Get all non-expired keys
        var matchingKeys = Store.Keys
            .Where(k => CommonHelper.MatchesPattern(k, pattern))
            .Select(k => RespValue.BulkString(k))
            .ToList()
            .AsReadOnly();

        await ctx.Writer.WriteAsync(RespValue.Array(matchingKeys));
    }

    private static async Task TypeAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'type' command");
            return;
        }

        var entry = Store.GetEntry(args[0]);
        string type = entry?.Type ?? "none";

        await ctx.Writer.WriteAsync(RespValue.SimpleString(type));
    }

    private static async Task GetMetaAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'getmeta' command");
            return;
        }

        if (!Store.TryGetMeta(args[0], out var meta))
        {
            await ctx.Writer.WriteAsync(RespValue.NullBulk);
            return;
        }

        var metaList = new List<RespValue>
        {
            RespValue.BulkString(meta.Type),
            RespValue.Integer(meta.TtlSeconds),
            RespValue.Integer(meta.EstimatedSizeBytes)
        }.AsReadOnly();

        await ctx.Writer.WriteAsync(RespValue.Array(metaList));
    }

    // ---------------- Lists ----------------
    private static async Task LPushAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            await Error(ctx, "ERR wrong number of arguments for 'lpush' command");
            return;
        }

        string key = args[0];
        var values = args.Skip(1).ToArray();

        var entry = Store.GetEntry(key);
        if (entry != null && entry is not ListEntry)
        {
            await Error(ctx, "WRONGTYPE Operation against a key holding the wrong kind of value");
            return;
        }

        int added = Store.LPush(key, values, persist: true);
        await ctx.Writer.WriteAsync(RespValue.Integer(added));
    }

    private static async Task RPushAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            await Error(ctx, "ERR wrong number of arguments for 'rpush' command");
            return;
        }

        string key = args[0];
        var values = args.Skip(1).ToArray();

        var entry = Store.GetEntry(key);
        if (entry != null && entry is not ListEntry)
        {
            await Error(ctx, "WRONGTYPE Operation against a key holding the wrong kind of value");
            return;
        }

        int added = Store.RPush(key, values, persist: true);
        await ctx.Writer.WriteAsync(RespValue.Integer(added));
    }

    private static async Task LPopAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'lpop' command");
            return;
        }

        string? popped = Store.LPop(args[0], persist: true);
        await ctx.Writer.WriteAsync(popped == null ? RespValue.NullBulk : RespValue.BulkString(popped));
    }

    private static async Task RPopAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'rpop' command");
            return;
        }

        string? popped = Store.RPop(args[0], persist: true);
        await ctx.Writer.WriteAsync(popped == null ? RespValue.NullBulk : RespValue.BulkString(popped));
    }

    private static async Task LLenAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'llen' command");
            return;
        }

        long len = Store.LLen(args[0]);
        await ctx.Writer.WriteAsync(RespValue.Integer(len));
    }

    private static async Task LRangeAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 3)
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error("ERR wrong number of arguments for 'lrange' command")
            );
            return;
        }

        string key = args[0];
        if (!long.TryParse(args[1], out long start) || !long.TryParse(args[2], out long stop))
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error("ERR value is not an integer or out of range")
            );
            return;
        }

        var entry = CommandRegistry.Store.GetEntry(key);
        if (entry == null)
        {
            await ctx.Writer.WriteAsync(RespValue.Array(Array.Empty<RespValue>().AsReadOnly()));
            return;
        }

        if (entry is not ListEntry listEntry)
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error("WRONGTYPE Operation against a key holding the wrong kind of value")
            );
            return;
        }

        var values = listEntry.Values;

        // Handle negative indices (Redis-style: -1 = last element)
        if (start < 0) start = values.Count + start;
        if (stop < 0) stop = values.Count + stop;

        // Clamp to bounds
        start = Math.Max(0, start);
        stop = Math.Min(values.Count - 1, stop);

        if (start > stop)
        {
            await ctx.Writer.WriteAsync(RespValue.Array(Array.Empty<RespValue>().AsReadOnly()));
            return;
        }

        var result = values
            .Skip((int)start)
            .Take((int)(stop - start + 1))
            .Select(v => RespValue.BulkString(v))
            .ToList()
            .AsReadOnly();

        await ctx.Writer.WriteAsync(RespValue.Array(result));
    }

    // ---------------- Hashes ----------------
    private static async Task HSetAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count < 3 || (args.Count - 1) % 2 != 0)
        {
            await Error(ctx, "ERR wrong number of arguments for 'hset' command");
            return;
        }

        string key = args[0];

        var entry = Store.GetEntry(key);
        if (entry != null && entry is not HashEntry)
        {
            await Error(ctx, "WRONGTYPE Operation against a key holding the wrong kind of value");
            return;
        }

        // Pass key + all remaining args as field-value pairs
        // Skip(1) removes the key itself
        int newCount = Store.HSet(key, persist: true, args.Skip(1).ToArray());

        await ctx.Writer.WriteAsync(RespValue.Integer(newCount));
    }

    private static async Task HGetAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 2)
        {
            await Error(ctx, "ERR wrong number of arguments for 'hget' command");
            return;
        }

        string? value = Store.HGet(args[0], args[1]);
        await ctx.Writer.WriteAsync(value == null ? RespValue.NullBulk : RespValue.BulkString(value));
    }

    private static async Task HDelAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 2)
        {
            await Error(ctx, "ERR wrong number of arguments for 'hdel' command");
            return;
        }

        bool deleted = Store.HDel(args[0], args[1], persist: true);
        await ctx.Writer.WriteAsync(RespValue.Integer(deleted ? 1 : 0));
    }

    private static async Task HLenAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'hlen' command");
            return;
        }

        long len = Store.HLen(args[0]);
        await ctx.Writer.WriteAsync(RespValue.Integer(len));
    }

    private static async Task HKeysAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'hkeys' command");
            return;
        }

        var keys = Store.HKeys(args[0]).Select(RespValue.BulkString).ToList().AsReadOnly();
        await ctx.Writer.WriteAsync(RespValue.Array(keys));
    }

    private static async Task HScanAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            await Error(ctx, "ERR wrong number of arguments for 'hscan' command");
            return;
        }

        string key = args[0];

        // Parse cursor (must be non-negative integer)
        if (!ulong.TryParse(args[1], out ulong cursor))
        {
            await Error(ctx, "ERR invalid cursor");
            return;
        }

        string? matchPattern = null;
        int countHint = 10; // default ~10 fields per iteration

        // Parse optional MATCH and COUNT
        for (int i = 2; i < args.Count; i += 2)
        {
            string opt = args[i].ToUpperInvariant();

            if (opt == "MATCH" && i + 1 < args.Count)
            {
                matchPattern = args[i + 1];
                i++; // skip value
            }
            else if (opt == "COUNT" && i + 1 < args.Count)
            {
                if (!int.TryParse(args[i + 1], out int cnt) || cnt <= 0)
                {
                    await Error(ctx, "ERR invalid COUNT value");
                    return;
                }
                countHint = cnt;
            }
            else
            {
                await Error(ctx, $"ERR syntax error near '{opt}'");
                return;
            }
        }

        // Get the hash entry
        var entry = Store.GetEntry(key);
        if (entry == null || entry is not HashEntry hashEntry)
        {
            // Key not found or wrong type → cursor 0 + empty array
            await ReplyWithCursorAndFields(ctx, 0, new List<RespValue>());
            return;
        }

        var allFields = hashEntry.Fields.ToList(); // field → value pairs

        if (allFields.Count == 0)
        {
            await ReplyWithCursorAndFields(ctx, 0, new List<RespValue>());
            return;
        }

        // Cursor logic (same as SCAN)
        int startIndex = (int)cursor;
        if (startIndex >= allFields.Count)
        {
            await ReplyWithCursorAndFields(ctx, 0, new List<RespValue>());
            return;
        }

        int endIndex = Math.Min(startIndex + countHint, allFields.Count);

        var resultFields = new List<RespValue>();

        for (int j = startIndex; j < endIndex; j++)
        {
            var kv = allFields[j];
            string field = kv.Key;

            // Apply MATCH filter on field name
            if (matchPattern == null || CommonHelper.MatchesPattern(field, matchPattern))
            {
                resultFields.Add(RespValue.BulkString(field));
                resultFields.Add(RespValue.BulkString(kv.Value));
            }
        }

        ulong nextCursor = (ulong)(endIndex < allFields.Count ? endIndex : 0);

        await ReplyWithCursorAndFields(ctx, nextCursor, resultFields);
    }

    private static async Task HValsAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'hvals' command");
            return;
        }

        var vals = Store.HVals(args[0]).Select(RespValue.BulkString).ToList().AsReadOnly();
        await ctx.Writer.WriteAsync(RespValue.Array(vals));
    }

    private static async Task HGetAllAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'hgetall' command");
            return;
        }

        string key = args[0];

        var entry = Store.GetEntry(key);
        if (entry == null)
        {
            // Key not found → empty array
            await ctx.Writer.WriteAsync(RespValue.Array(Array.Empty<RespValue>().AsReadOnly()));
            return;
        }

        if (entry is not HashEntry hashEntry)
        {
            await Error(ctx, "WRONGTYPE Operation against a key holding the wrong kind of value");
            return;
        }

        var allFields = new List<RespValue>();

        // Add each field and its value (flat list)
        foreach (var kv in hashEntry.Fields)
        {
            allFields.Add(RespValue.BulkString(kv.Key));
            allFields.Add(RespValue.BulkString(kv.Value));
        }

        await ctx.Writer.WriteAsync(RespValue.Array(allFields.AsReadOnly()));
    }


    // ---------------- Helpers ----------------

    private static Task Ok(CommandContext ctx)
        => ctx.Writer.WriteAsync(RespValue.SimpleString("OK"));

    private static Task Error(CommandContext ctx, string message)
        => ctx.Writer.WriteAsync(RespValue.Error(message));

    private static async Task ReplyWithCursorAndKeys(CommandContext ctx, ulong cursor, List<RespValue> keys)
    {
        var reply = new List<RespValue>
        {
            RespValue.BulkString(cursor.ToString()), // Cursor as bulk string
            RespValue.Array(keys.AsReadOnly())
        }.AsReadOnly();

        await ctx.Writer.WriteAsync(RespValue.Array(reply));
    }

    private static async Task ReplyWithCursorAndFields(CommandContext ctx, ulong cursor, List<RespValue> fields)
    {
        var reply = new List<RespValue>
    {
        RespValue.BulkString(cursor.ToString()),
        RespValue.Array(fields.AsReadOnly())
    };

        await ctx.Writer.WriteAsync(RespValue.Array(reply.AsReadOnly()));
    }

    private static string? GetConfigValue(string key)
    {
        return key switch
        {
            // Most common ones clients ask for
            "port" => _runtimeInfo?.Port.ToString() ?? "6380",
            "bind" => "127.0.0.1",  // or "*" / "0.0.0.0" if you allow all
            "maxmemory" => "0",
            "maxmemory-policy" => "noeviction",
            "timeout" => "0",          // 0 = no timeout
            "databases" => "1",          // you currently have only db0
            "appendonly" => "yes",
            "aof-enabled" => "yes",
            "requirepass" => "",           // or "no" / null
            "protected-mode" => "yes",
            "aof-use-no-appendfsync-on-rewrite" => "no",

            // Optional – nice to have
            "tcp-keepalive" => "0",
            "hz" => "10",
            "loglevel" => "notice",
            "slowlog-log-slower-than" => "10000",
            "slowlog-max-len" => "128",
            "lua-time-limit" => "5000",

            // Unknown / not implemented
            _ => null
        };
    }

}