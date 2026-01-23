namespace DevCache.Server;

public class DevCacheOptions
{
    public string? Password { get; set; }          // null = no auth required
    public int Port { get; set; } = 6379;
    public string AofFilePath { get; set; } = "devcache.aof";
    // ... other options
}