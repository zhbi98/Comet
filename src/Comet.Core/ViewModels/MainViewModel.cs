using Comet.Services.Abstractions;

namespace Comet.ViewModels;

/// <summary>
/// Root view model that exposes the feature-specific state used by MainPage.
/// </summary>
public sealed class MainViewModel : IDisposable
{
    public MainViewModel(
        ISerialPortService serialPortService,
        IAppSettingsStorageService appSettingsStorageService,
        ICommandPresetStorageService commandPresetStorageService,
        IRawReceiveRecordingService rawReceiveRecordingService,
        Func<Action, IPeriodicTimer> scheduledTimerFactory)
    {
        UserSettings = new UserSettingsViewModel(appSettingsStorageService);

        // Feature view models share the same connection instance so direct, preset,
        // terminal, and scheduled sends observe one transport lifecycle.
        Connection = new ConnectionViewModel(serialPortService);
        Terminal = new TerminalViewModel();
        TerminalAppearance = new TerminalAppearanceViewModel();
        TerminalAppearance.FontFamilyName = UserSettings.Current.Terminal.FontFamilyName;
        TerminalAppearance.FontSize = UserSettings.Current.Terminal.FontSize;
        Transmission = new TransmissionViewModel();
        CommandPresets = new CommandPresetsViewModel(commandPresetStorageService);
        ReceiveRecording = new ReceiveRecordingViewModel(rawReceiveRecordingService);
        ScheduledSending = new ScheduledSendViewModel(Connection, scheduledTimerFactory);
    }

    public UserSettingsViewModel UserSettings { get; }

    public ConnectionViewModel Connection { get; }

    public TerminalViewModel Terminal { get; }

    public TerminalAppearanceViewModel TerminalAppearance { get; }

    public TransmissionViewModel Transmission { get; }

    public CommandPresetsViewModel CommandPresets { get; }

    public ReceiveRecordingViewModel ReceiveRecording { get; }

    public ScheduledSendViewModel ScheduledSending { get; }

    public void Dispose()
    {
        // Stop the timer thread before disposing the serial service it may call.
        ScheduledSending.Dispose();
        ReceiveRecording.Dispose();
        Connection.Dispose();
    }
}
