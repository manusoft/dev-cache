namespace DevCache.UI;

public class CacheItem
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Type { get; set; } = "string";
    public int TtlSeconds { get; set; }
    public int SizeBytes { get; set; }
}
