namespace ManuHub.Memora.Models;

public record ServerRuntimeInfo(
    int Port,
    DateTime StartedAt,
    string? ConfigFile = null,
    long MaxMemoryBytes = 0,
    string MaxMemoryPolicy = "noeviction"
);