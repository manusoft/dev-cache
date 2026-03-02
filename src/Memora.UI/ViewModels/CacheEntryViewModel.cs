using CommunityToolkit.Mvvm.ComponentModel;

namespace DevCache.UI;

public partial class CacheEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string key = "";

    [ObservableProperty]
    private string type = "string";

    [ObservableProperty]
    private int ttlSeconds;

    [ObservableProperty]
    private int sizeBytes;

    [ObservableProperty]
    private string? value;          // loaded on demand

    public bool IsExpired => TtlSeconds == -2;
    public bool HasValue => Value != null;
}