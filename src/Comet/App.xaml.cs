using Comet.Recording;
using Comet.Services;
using Comet.Services.Timing;
using Comet.ViewModels;
using Comet.Views;
using Microsoft.UI.Xaml;

namespace Comet;

public partial class App : Application
{
    private Window? _window;

    public static Window? CurrentWindow => (Current as App)?._window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // App is the composition root: only this layer selects concrete Windows
        // services before injecting the UI-independent root view model.
        var viewModel = new MainViewModel(
            new SerialPortService(),
            new CommandPresetStorageService(),
            new RawReceiveRecordingService(),
            callback => new HighResolutionPeriodicTimer(callback));
        _window = new MainWindow(viewModel);
        _window.Activate();
    }
}
