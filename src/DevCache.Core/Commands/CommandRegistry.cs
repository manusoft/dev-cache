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

    public static InMemoryStore Store => _store
        ?? throw new InvalidOperationException("Store not initialized");

    public static bool TryGet(string name, out RedisCommand command)
        => _commands.TryGetValue(name, out command!);

    static CommandRegistry()
    {
        _commands = new(StringComparer.OrdinalIgnoreCase)
        {
            // Core
            ["PING"] = PingAsync,
            ["ECHO"] = EchoAsync,
            ["INFO"] = InfoAsync,

            // KV (Strings)
            ["SET"] = SetAsync,
            ["GET"] = GetAsync,
            ["DEL"] = DelAsync,
            ["EXISTS"] = ExistsAsync,

            // TTL
            ["EXPIRE"] = ExpireAsync,
            ["TTL"] = TtlAsync,
            ["PEXPIRE"] = PExpireAsync,
            ["PTTL"] = PTtlAsync,

            // DB
            ["FLUSHDB"] = FlushDbAsync,

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

            // Hashes
            ["HSET"] = HSetAsync,
            ["HGET"] = HGetAsync,
            ["HDEL"] = HDelAsync,
            ["HLEN"] = HLenAsync,
            ["HKEYS"] = HKeysAsync,
            ["HVALS"] = HValsAsync,
        };
    }

    public static void Initialize(InMemoryStore store, ServerRuntimeInfo runtimeInfo)
    {
        if (_store != null) throw new InvalidOperationException("Already initialized");
        _store = store;
        _runtimeInfo = runtimeInfo;
    }

    // ---------------- Core ----------------

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
            await Error(ctx, "ERR wrong number of arguments for 'flushdb' command");
            return;
        }

        Store.FlushAll();
        await Ok(ctx);
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

    // ---------------- Hashes ----------------
    private static async Task HSetAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 3)
        {
            await Error(ctx, "ERR wrong number of arguments for 'hset' command");
            return;
        }

        string key = args[0];
        string field = args[1];
        string value = args[2];

        var entry = Store.GetEntry(key);
        if (entry != null && entry is not HashEntry)
        {
            await Error(ctx, "WRONGTYPE Operation against a key holding the wrong kind of value");
            return;
        }

        bool added = Store.HSet(key, field, value, persist: true);
        await ctx.Writer.WriteAsync(RespValue.Integer(added ? 1 : 0));
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

    // ---------------- Helpers ----------------
    private static Task Ok(CommandContext ctx)
        => ctx.Writer.WriteAsync(RespValue.SimpleString("OK"));

    private static Task Error(CommandContext ctx, string message)
        => ctx.Writer.WriteAsync(RespValue.Error(message));

}