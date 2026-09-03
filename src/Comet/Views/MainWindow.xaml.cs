using Comet.ViewModels;
using Comet.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Comet.Views;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly WindowIconManager? _windowIconManager;
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        TitleBarIconImage.Source = WindowIconManager.CreateTitleBarImageSource();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        _windowIconManager = WindowIconManager.Attach(WinRT.Interop.WindowNative.GetWindowHandle(this));
        Closed += MainWindow_Closed;

        InitializeTerminalAppearance();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1140;
            presenter.PreferredMinimumHeight = 640;
        }

        var windowSize = new SizeInt32(1200, 720);
        AppWindow.Resize(windowSize);
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is not null)
        {
            var workArea = displayArea.WorkArea;
            AppWindow.Move(new PointInt32(
                workArea.X + Math.Max(0, (workArea.Width - windowSize.Width) / 2),
                workArea.Y + Math.Max(0, (workArea.Height - windowSize.Height) / 2)));
        }

        RootFrame.Content = new MainPage(viewModel);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (RootFrame.Content is MainPage mainPage)
        {
            mainPage.Shutdown();
        }

        _windowIconManager?.Dispose();
    }

    public void SetConnectionStatus(string? portName)
    {
        Title = string.IsNullOrWhiteSpace(portName)
            ? "Comet"
            : $"Comet · {portName} 已连接";
    }
}
