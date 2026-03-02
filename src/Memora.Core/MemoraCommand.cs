namespace ManuHub.Memora;

public delegate Task MemoraCommand(CommandContext context, IReadOnlyList<string> args);