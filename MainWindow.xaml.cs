using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Comet;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "CometTerminalIcon.ico");
        AppWindow.SetIcon(iconPath);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1200;
            presenter.PreferredMinimumHeight = 720;
        }

        var windowSize = new SizeInt32(1280, 820);
        AppWindow.Resize(windowSize);
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is not null)
        {
            var workArea = displayArea.WorkArea;
            AppWindow.Move(new PointInt32(
                workArea.X + Math.Max(0, (workArea.Width - windowSize.Width) / 2),
                workArea.Y + Math.Max(0, (workArea.Height - windowSize.Height) / 2)));
        }

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    public void SetConnectionStatus(string? portName)
    {
        Title = string.IsNullOrWhiteSpace(portName)
            ? "Comet"
            : $"Comet · {portName} 已连接";
    }
}
