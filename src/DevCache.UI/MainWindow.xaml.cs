using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DevCache.UI;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly DevCacheClient _client = new();
    private readonly ObservableCollection<CacheItem> _cacheItems = new();

    public ObservableCollection<string> Keys { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        //_client = new DevCacheClient();
        Debug.WriteLine("Client initialized and connected.");

        CreateAppWindow();

       
        //CacheDataGrid.ItemsSource = _cacheItems;

        //_ = RefreshCacheAsync();

        // Debug test
        _ = TestDevCacheAsync();
    }

    private async Task TestDevCacheAsync()
    {
        try
        {
            // Set a key
            var setResult = await _client.SetAsync("debugKey", "hello");
            Debug.WriteLine($"SET debugKey => {setResult}");

            // Get the key
            var value = await _client.GetAsync("debugKey");
            Debug.WriteLine($"GET debugKey => {value}");

            // List keys
            var keys = await _client.KeysAsync();
            Debug.WriteLine("KEYS:");
            foreach (var k in keys)
                Console.WriteLine($" - {k}");

            // Get metadata (if supported)
            var meta = await _client.GetMetaAsync("debugKey");
            if (meta != null)
            {
                Debug.WriteLine($"GETMETA debugKey => Type={meta.Type}, TTL={meta.TtlSeconds}, Size={meta.SizeBytes}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DevCache test failed: {ex}");
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCacheAsync();
    }

    private async Task RefreshCacheAsync()
    {
        while (true)
        {
            try
            {
                var keys = await _client.KeysAsync();

                // Clear existing items
                _cacheItems.Clear();

                foreach (var key in keys)
                {
                    // Use the async-friendly GetMetaAsync
                    var meta = await _client.GetMetaAsync(key);
                    if (meta == null)
                        continue;

                    var value = await _client.GetAsync(key) ?? "";

                    _cacheItems.Add(new CacheItem
                    {
                        Key = key,
                        Value = value,
                        Type = meta.Type,
                        TtlSeconds = meta.TtlSeconds,
                        SizeBytes = meta.SizeBytes
                    });
                }
            }
            catch
            {
                // Ignore connection errors for now
            }

            await Task.Delay(1000); // refresh every second
        }
    }


    private void CreateAppWindow()
    {
        // Set the window title
        AppWindow.Title = "This is a title";

        // Set the window size (including borders)
        AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 600));

        // Set the taskbar icon (displayed in the taskbar)
        //AppWindow.SetIcon("Assets/Tiles/GalleryIcon.ico");

        // Set the title bar icon (displayed in the window's title bar)
        //AppWindow.SetTitleBarIcon("Assets/Tiles/GalleryIcon.ico");

        // Set the window icon (affects both taskbar and title bar, can be omitted if the above two are set)
        // AppWindow.SetIcon("Assets/Tiles/GalleryIcon.ico");


        // Set the preferred theme for the title bar
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

        OverlappedPresenter presenter = OverlappedPresenter.Create();
        presenter.PreferredMinimumWidth = 800;
        presenter.PreferredMinimumHeight = 600;
        presenter.PreferredMaximumWidth = 800;
        presenter.PreferredMaximumHeight = 600;
        presenter.IsMaximizable = false;

        AppWindow.SetPresenter(presenter);
        // Center the window on the screen.
        CenterWindow();
    }

    // Centers the given AppWindow on the screen based on the available display area.
    private void CenterWindow()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest)?.WorkArea;
        if (area == null) return;
        AppWindow.Move(new PointInt32((area.Value.Width - AppWindow.Size.Width) / 2, (area.Value.Height - AppWindow.Size.Height) / 2));
    }

    private async void SetButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(KeyTextBox.Text))
        {
            await _client.SetAsync(KeyTextBox.Text, ValueTextBox.Text);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (CacheDataGrid.SelectedItem is CacheItem item)
        {
            await _client.DeleteAsync(item.Key);
        }
    }

    // Helper to show messages
    private void AppMessage(string text)
    {
        Debug.WriteLine(text);
        //_ = DispatcherQueue.TryEnqueue(() =>
        //MessageBox.Show(text)); // or a StatusBar / TextBlock for nicer UI

    }
}
