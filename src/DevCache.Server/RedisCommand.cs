namespace DevCache;


public delegate Task RedisCommand(CommandContext context, IReadOnlyList<string> args);