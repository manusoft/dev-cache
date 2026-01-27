namespace DevCache.Core.Models;

public record ServerRuntimeInfo(
    int Port,
    DateTime StartedAt,
    string? ConfigFile = null,
    long MaxMemoryBytes = 0,
    string MaxMemoryPolicy = "noeviction"
);