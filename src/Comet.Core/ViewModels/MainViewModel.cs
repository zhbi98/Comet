using Comet.Services.Abstractions;

namespace Comet.ViewModels;

/// <summary>
/// Root view model that exposes the feature-specific state used by MainPage.
/// </summary>
public sealed class MainViewModel : IDisposable
{
    public MainViewModel(
        ISerialPortService serialPortService,
        ICommandPresetStorageService commandPresetStorageService,
        Func<Action, IPeriodicTimer> repeatTimerFactory)
    {
        // Feature view models share the same connection instance so direct, preset,
        // terminal, and repeated sends observe one transport lifecycle.
        Connection = new ConnectionViewModel(serialPortService);
        Terminal = new TerminalViewModel();
        Transmission = new TransmissionViewModel();
        CommandPresets = new CommandPresetsViewModel(commandPresetStorageService);
        RepeatSending = new RepeatSendViewModel(Connection, repeatTimerFactory);
    }

    public ConnectionViewModel Connection { get; }

    public TerminalViewModel Terminal { get; }

    public TransmissionViewModel Transmission { get; }

    public CommandPresetsViewModel CommandPresets { get; }

    public RepeatSendViewModel RepeatSending { get; }

    public void Dispose()
    {
        // Stop the timer thread before disposing the serial service it may call.
        RepeatSending.Dispose();
        Connection.Dispose();
    }
}
