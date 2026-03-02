namespace ManuHub.Memora;

public class MemoraOptions
{
    public string? Password { get; set; }          // null = no auth required
    public int Port { get; set; } = 6379;
    public string AofFilePath { get; set; } = "memora.aof";
    // ... other options
}