using ManuHub.Memora.Common;

namespace ManuHub.Memora.Commands.Core;

internal static class AuthCommand
{
    public static async Task Execute(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            await ctx.Writer.WriteAsync(
                RespValue.Error("ERR wrong number of arguments for 'auth' command"));
            return;
        }

        string provided = args[0];

        // If no password is required → always accept
        if (CommandRegistry.RequirePass == null || string.IsNullOrEmpty(CommandRegistry.RequirePass))
        {
            ctx.IsAuthenticated = true;
            await ctx.Writer.WriteAsync(RespValue.SimpleString("OK"));
            return;
        }

        // Real check (simple string comparison is fine for now)
        if (provided == CommandRegistry.RequirePass)
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
}