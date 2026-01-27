using DevCache.UI.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;


namespace DevCache.UI;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var mainPage = new MainPage();
        this.MainFrame.Content = mainPage;
        CreateAppWindow();
    }

    private void CreateAppWindow()
    {
        // Set the window title
        AppWindow.Title = "DevCache Explorer";

        // Set the window size (including borders)
        AppWindow.Resize(new SizeInt32(1300, 850));

        // Set the taskbar icon (displayed in the taskbar)
        AppWindow.SetIcon("Assets/appicon.ico");

        // Set the title bar icon (displayed in the window's title bar)
        //AppWindow.SetTitleBarIcon("Assets/Tiles/GalleryIcon.ico");

        // Set the window icon (affects both taskbar and title bar, can be omitted if the above two are set)
        // AppWindow.SetIcon("Assets/Tiles/GalleryIcon.ico");


        // Set the preferred theme for the title bar
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

        OverlappedPresenter presenter = OverlappedPresenter.Create();
        presenter.PreferredMinimumWidth = 1200;
        presenter.PreferredMinimumHeight = 800;
        presenter.PreferredMaximumWidth = 1200;
        presenter.PreferredMaximumHeight = 800;
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
}
