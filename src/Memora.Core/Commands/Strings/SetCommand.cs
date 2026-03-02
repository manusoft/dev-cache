using ManuHub.Memora.Common;

namespace ManuHub.Memora.Commands.Strings;

internal static class SetCommand
{
    public static async Task Execute(CommandContext ctx, IReadOnlyList<string> args)
    {
        // Require key + value (value can be empty)
        if (args.Count < 2)
        {
            await CommandRegistry.Error(ctx, "ERR wrong number of arguments for 'set' command");
            return;
        }

        string key = args[0];
        string value = args[1];  // value can be ""

        bool? nx = null;
        long? expireMs = null;

        // Start option parsing AFTER value (index 2+)
        int i = 2;

        while (i < args.Count)
        {
            string opt = args[i].ToUpperInvariant();

            if (string.IsNullOrEmpty(opt))
            {
                i++;
                continue;  // ignore empty args from CLI
            }

            if (opt == "EX")
            {
                if (i + 1 >= args.Count || !int.TryParse(args[i + 1], out int sec) || sec <= 0)
                {
                    await CommandRegistry.Error(ctx, "ERR value is not an integer or out of range");
                    return;
                }
                expireMs = sec * 1000L;
                i += 2;
            }
            else if (opt == "PX")
            {
                if (i + 1 >= args.Count || !long.TryParse(args[i + 1], out long ms) || ms <= 0)
                {
                    await CommandRegistry.Error(ctx, "ERR value is not an integer or out of range");
                    return;
                }
                expireMs = ms;
                i += 2;
            }
            else if (opt == "NX")
            {
                nx = true;
                i++;
            }
            else if (opt == "XX")
            {
                nx = false;
                i++;
            }
            else
            {
                await CommandRegistry.Error(ctx, $"ERR syntax error near '{opt}'");
                return;
            }
        }

        bool keyExists = CommandRegistry.Store.Exists(key);

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

        CommandRegistry.Store.Set(key, value, persist: true);

        if (expireMs.HasValue)
        {
            CommandRegistry.Store.Expire(key, expireMs.Value, persist: true);
        }

        await ctx.Writer.WriteAsync(RespValue.SimpleString("OK"));
    }
}