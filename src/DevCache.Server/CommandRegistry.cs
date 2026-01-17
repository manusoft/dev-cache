namespace DevCache;

public static class CommandRegistry
{
    private static readonly Dictionary<string, RedisCommand> _commands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["PING"] = PingAsync,
            ["ECHO"] = EchoAsync
        };

    public static bool TryGet(string name, out RedisCommand command)
        => _commands.TryGetValue(name, out command!);

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
}