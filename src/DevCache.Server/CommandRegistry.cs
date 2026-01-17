namespace DevCache;

public static class CommandRegistry
{
    private static readonly Dictionary<string, RedisCommand> _commands;
       

    private static readonly InMemoryStore Store = new();

    public static bool TryGet(string name, out RedisCommand command)
        => _commands.TryGetValue(name, out command!);

    static CommandRegistry()
    {
        _commands = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PING"] = PingAsync,
            ["ECHO"] = EchoAsync,
            ["SET"] = SetAsync,
            ["GET"] = GetAsync,
            ["DEL"] = DelAsync,
            ["EXISTS"] = ExistsAsync,
            ["FLUSHDB"] = FlushDbAsync,
            ["EXPIRE"] = ExpireAsync,
            ["TTL"] = TtlAsync,
        };
    }

    // ---------------- Commands ----------------

    private static async Task PingAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count > 1)
        {
            await ctx.Writer.WriteAsync(
                ctx.Stream,
                RespValue.Error("ERR wrong number of arguments for 'ping' command"));
            return;
        }

        var message = args.Count == 1 ? args[0] : "PONG";
        await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Simple(message));
    }

    private static async Task EchoAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await ctx.Writer.WriteAsync(
                ctx.Stream,
                RespValue.Error("ERR wrong number of arguments for 'echo' command"));
            return;
        }

        await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Bulk(args[0]));
    }

    private static async Task SetAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 2)
        {
            await ctx.Writer.WriteAsync(ctx.Stream,
                RespValue.Error("ERR wrong number of arguments for 'set' command"));
            return;
        }

        Store.Set(args[0], args[1]);
        await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Simple("OK"));
    }

    private static async Task GetAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await ctx.Writer.WriteAsync(ctx.Stream,
                RespValue.Error("ERR wrong number of arguments for 'get' command"));
            return;
        }

        var value = Store.Get(args[0]);
        if (value == null)
            await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Null());
        else
            await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Bulk(value));
    }

    private static async Task DelAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await ctx.Writer.WriteAsync(ctx.Stream,
                RespValue.Error("ERR wrong number of arguments for 'del' command"));
            return;
        }

        var removed = Store.Del(args[0]) ? 1L : 0L;
        await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Integer(removed));
    }

    private static async Task ExistsAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await ctx.Writer.WriteAsync(ctx.Stream,
                RespValue.Error("ERR wrong number of arguments for 'exists' command"));
            return;
        }

        var exists = Store.Exists(args[0]) ? 1L : 0L;
        await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Integer(exists));
    }

    private static async Task FlushDbAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 0)
        {
            await ctx.Writer.WriteAsync(ctx.Stream,
                RespValue.Error("ERR wrong number of arguments for 'flushdb' command"));
            return;
        }

        Store.FlushAll();
        await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Simple("OK"));
    }

    private static async Task ExpireAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 2)
        {
            await ctx.Writer.WriteAsync(ctx.Stream,
                RespValue.Error("ERR wrong number of arguments for 'expire' command"));
            return;
        }

        if (!int.TryParse(args[1], out var seconds) || seconds < 0)
        {
            await ctx.Writer.WriteAsync(ctx.Stream,
                RespValue.Error("ERR invalid expire time"));
            return;
        }

        var result = Store.Expire(args[0], seconds) ? 1L : 0L;
        await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Integer(result));
    }

    private static async Task TtlAsync(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await ctx.Writer.WriteAsync(ctx.Stream,
                RespValue.Error("ERR wrong number of arguments for 'ttl' command"));
            return;
        }

        var ttl = Store.TTL(args[0]);
        await ctx.Writer.WriteAsync(ctx.Stream, RespValue.Integer(ttl));
    }
}