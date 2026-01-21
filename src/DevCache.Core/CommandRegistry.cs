using DevCache.Shared;

namespace DevCache.Core;

public static class CommandRegistry
{
    private static readonly Dictionary<string, RedisCommand> _commands;
    private static InMemoryStore? _store;
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

            // KV
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
            ["GETMETA"] = GetMetaAsync
        };
    }

    public static void Initialize(InMemoryStore store)
    {
        if (_store != null) throw new InvalidOperationException("Already initialized");
        _store = store;
    }

    // ---------------- Core ----------------

    private static async Task PingAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count > 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'ping' command");
            return;
        }

        await ctx.Writer.WriteAsync(
            ctx.Stream,
            RespValue.Simple(args.Count == 1 ? args[0] : "PONG"));
    }

    private static async Task EchoAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'echo' command");
            return;
        }

        await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Bulk(args[0]));
    }

    // ---------------- KV ----------------
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
            await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Null());
            return;
        }

        if (nx == false && !keyExists)
        {
            await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Null());
            return;
        }

        // Set the value FIRST
        Store.Set(key, value, persist: true);

        // Apply expiry AFTER set (so key exists)
        if (expireMs.HasValue)
        {
            Store.Expire(key, expireMs.Value, persist: true);
        }

        await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Simple("OK"));
    }

    private static async Task GetAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'get' command");
            return;
        }

        var value = Store.Get(args[0]);

        await ctx.Writer.WriteAsync(
            ctx.Stream,
            value == null ? RespValue.Null() : RespValue.Bulk(value));
    }

    private static async Task DelAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'del' command");
            return;
        }

        await ctx.Writer.WriteAsync(
            ctx.Stream,
            RespValue.Integer(Store.Del(args[0]) ? 1 : 0));
    }

    private static async Task ExistsAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'exists' command");
            return;
        }

        await ctx.Writer.WriteAsync(
            ctx.Stream,
            RespValue.Integer(Store.Exists(args[0]) ? 1 : 0));
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

        await ctx.Writer.WriteAsync(
            ctx.Stream,
            RespValue.Integer(Store.Expire(args[0], seconds) ? 1 : 0));
    }

    private static async Task TtlAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'ttl' command");
            return;
        }

        await ctx.Writer.WriteAsync(
            ctx.Stream,
            RespValue.Integer(Store.GetTtls(args[0])));
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

        await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Integer(Store.Expire(args[0], ms, persist: true) ? 1 : 0));
    }

    private static async Task PTtlAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await Error(ctx, "ERR wrong number of arguments for 'pttl' command");
            return;
        }

        await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Integer(Store.GetTtlMs(args[0])));
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
        string pattern;

        if (args.Count == 0)
        {
            pattern = "*";           // redis-cli default behavior: KEYS without arg = KEYS *
        }
        else if (args.Count == 1)
        {
            pattern = args[0];
        }
        else
        {
            await ctx.Writer.WriteAsync(ctx.Stream,
                RespValue.Error("ERR wrong number of arguments for 'keys' command"));
            return;
        }

        if (pattern != "*")
        {
            await ctx.Writer.WriteAsync(ctx.Stream,
                RespValue.Error($"ERR pattern '{pattern}' not supported (only '*' allowed in this version)"));
            return;
        }

        var keyList = Store.Keys
            .Select(k => RespValue.Bulk(k))
            .ToList()
            .AsReadOnly();

        await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Array(keyList));
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
            await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Null());
            return;
        }

        var metaList = new List<RespValue>
        {
            RespValue.Bulk(meta.Type),
            RespValue.Integer(meta.TtlSeconds),
            RespValue.Integer(meta.SizeBytes)
        }.AsReadOnly();

        await ctx.Writer.WriteAsync(
            ctx.Stream,
            RespValue.Array(metaList)
        );
    }


    // ---------------- Helpers ----------------
    private static Task Ok(CommandContext ctx)
        => ctx.Writer.WriteAsync(ctx.Stream, RespValue.Simple("OK"));

    private static Task Error(CommandContext ctx, string message)
        => ctx.Writer.WriteAsync(ctx.Stream, RespValue.Error(message));

    private static string EscapeForAof(string s)
    {
        // Very naive escaping – real Redis uses proper RESP quoting
        // For now: replace " with \"
        return s.Replace("\"", "\\\"").Replace("\n", "\\n");
    }

}