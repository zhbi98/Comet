using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;
using Comet.Models;
using Comet.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Comet.Views;

public sealed partial class MainPage : Page
{
    private const double CompactToolbarWidth = 700;
    private const double ExpandedShellColumnSpacing = 14;

    private static readonly GridLength ExpandedConnectionPanelWidth = new(256);

    private readonly ConcurrentQueue<SerialBytesReceivedEventArgs> _receiveQueue;
    private readonly DispatcherQueueTimer _terminalRenderTimer;
    private readonly SolidColorBrush _connectedBrush = new(Windows.UI.Color.FromArgb(255, 22, 135, 93));
    private readonly SolidColorBrush _disconnectedBrush = new(Windows.UI.Color.FromArgb(255, 102, 118, 138));

    private int _receiveDrainScheduled;
    private readonly StringBuilder _pendingTerminalText = new();
    private bool _isTerminalRenderPending;
    private long? _lastReceiveTimestamp;
    private bool _isUnloaded;
    private int _shutdownState;
    private bool _isCompactTerminalToolbar;
    private bool _isConnectionPanelCollapsed;

    public MainViewModel ViewModel { get; }

    public MainPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        // DataContext supports conventional bindings; x:Bind continues to use the
        // strongly typed ViewModel property exposed by this page.
        DataContext = ViewModel;
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        _receiveQueue = new ConcurrentQueue<SerialBytesReceivedEventArgs>();

        SendTextBox.TextChanged += (_, _) => UpdateRepeatSendPayload();

        _terminalRenderTimer = DispatcherQueue.CreateTimer();
        _terminalRenderTimer.Interval = TimeSpan.FromMilliseconds(50);
        _terminalRenderTimer.IsRepeating = false;
        _terminalRenderTimer.Tick += TerminalRenderTimer_Tick;

        TerminalView.InputReceived += (_, args) => SendTerminalInput(args.Text);
        TerminalView.ViewportChanged += (_, _) => UpdateTerminalItemStatus();
        ViewModel.TerminalAppearance.PropertyChanged += TerminalAppearance_PropertyChanged;
        ViewModel.CommandPresets.PropertyChanged += CommandPresets_PropertyChanged;
        ApplyTerminalAppearance();
        ViewModel.Connection.BytesReceived += SerialPort_BytesReceived;
        ViewModel.Connection.ErrorOccurred += SerialPort_ErrorOccurred;
        ViewModel.ReceiveRecording.StateChanged += ReceiveRecording_StateChanged;
        ViewModel.ReceiveRecording.RecordingFailed += ReceiveRecording_RecordingFailed;
        ViewModel.ScheduledSending.PayloadSent += ScheduledSending_PayloadSent;
        ViewModel.ScheduledSending.SendFailed += ScheduledSending_SendFailed;

        InitializeSerialOptions();
        ApplyUserSettings();
        AttachUserSettingsChangeHandlers();
        InitializeCommandPresets();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = false;
        RunWithoutUserSettingsSave(() =>
        {
            RefreshPorts();
            ApplyStoredPortSelection();
        });
        UpdateConnectionState();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        Shutdown();
    }

    private void TerminalToolbarGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTerminalToolbarLayout();
    }

    private void ConnectionPanelCollapseButton_Click(object sender, RoutedEventArgs e)
    {
        SetConnectionPanelCollapsed(true);
    }

    private void ConnectionPanelExpandButton_Click(object sender, RoutedEventArgs e)
    {
        SetConnectionPanelCollapsed(false);
    }

    private void SetConnectionPanelCollapsed(bool isCollapsed)
    {
        if (_isConnectionPanelCollapsed == isCollapsed)
        {
            return;
        }

        _isConnectionPanelCollapsed = isCollapsed;
        ConnectionPanel.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ConnectionPanelColumn.Width = isCollapsed ? new GridLength(0) : ExpandedConnectionPanelWidth;
        ConnectionPanelExpandButton.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
        ShellContentGrid.ColumnSpacing = isCollapsed ? 0 : ExpandedShellColumnSpacing;
    }

    private void UpdateTerminalToolbarLayout()
    {
        var isCompact = TerminalToolbarGrid.ActualWidth > 0 &&
                        TerminalToolbarGrid.ActualWidth < CompactToolbarWidth;
        if (_isCompactTerminalToolbar == isCompact)
        {
            return;
        }

        _isCompactTerminalToolbar = isCompact;
        // Keep the toolbar on one line. As the terminal column narrows, reduce the
        // gap between the title and action group before reducing button dimensions.
        TerminalToolbarGrid.ColumnSpacing = isCompact ? 2 : 8;
        TerminalToolbarActions.Spacing = isCompact ? 3 : 5;

        // Compact mode keeps every action reachable inside the reduced terminal width.
        PresetPanelToggleButton.Width = isCompact ? 30 : 34;
        SaveLogButton.Width = isCompact ? 30 : 34;
        ClearTerminalButton.Width = isCompact ? 30 : 34;
    }

    internal void Shutdown()
    {
        // Both Page.Unloaded and the window close callback can reach this method.
        // Serial and native timer resources must be released exactly once.
        if (Interlocked.Exchange(ref _shutdownState, 1) != 0)
        {
            return;
        }

        _isUnloaded = true;
        SaveUserSettings();
        ExitPresetReorderMode();
        ViewModel.TerminalAppearance.PropertyChanged -= TerminalAppearance_PropertyChanged;
        ViewModel.CommandPresets.PropertyChanged -= CommandPresets_PropertyChanged;
        ViewModel.ReceiveRecording.StateChanged -= ReceiveRecording_StateChanged;
        ViewModel.ReceiveRecording.RecordingFailed -= ReceiveRecording_RecordingFailed;
        StopScheduledSending();
        _terminalRenderTimer.Stop();
        try
        {
            ViewModel.Connection.Close();
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            // Closing the window must continue even if the device disappeared.
        }
        finally
        {
            ViewModel.Dispose();
        }
    }

    private void TerminalAppearance_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(TerminalAppearanceViewModel.FontFamilyName) or
            nameof(TerminalAppearanceViewModel.FontSize))
        {
            ApplyTerminalAppearance();
            SaveUserSettings();
        }
    }

    private void ApplyTerminalAppearance()
    {
        TerminalView.ApplyTypography(
            new FontFamily(ViewModel.TerminalAppearance.FontFamilyName),
            ViewModel.TerminalAppearance.FontSize);
    }

    private void InitializeSerialOptions()
    {
        BaudRateComboBox.ItemsSource = new[] { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };
        BaudRateComboBox.SelectedItem = SerialSettingsModel.DEFAULT_BAUD_RATE;
        DataBitsComboBox.ItemsSource = new[] { 5, 6, 7, 8 };
        DataBitsComboBox.SelectedItem = SerialSettingsModel.DEFAULT_DATA_BITS;
        StopBitsComboBox.ItemsSource = new[] { "1", "1.5", "2" };
        StopBitsComboBox.SelectedItem = SerialSettingsModel.DEFAULT_STOP_BITS;
        ParityComboBox.ItemsSource = new[] { "None", "Odd", "Even", "Mark", "Space" };
        ParityComboBox.SelectedItem = SerialSettingsModel.DEFAULT_PARITY;
        HandshakeComboBox.ItemsSource = new[] { "None", "XOn/XOff", "RTS/CTS", "RTS/CTS + XOn/XOff" };
        HandshakeComboBox.SelectedItem = SerialSettingsModel.DEFAULT_HANDSHAKE;
        EncodingComboBox.ItemsSource = new[] { "UTF-8", "GBK", "ASCII" };
        EncodingComboBox.SelectedItem = SerialSettingsModel.DEFAULT_ENCODING_NAME;
        LineEndingComboBox.ItemsSource = new[] { "无", "CRLF", "CR", "LF" };
        LineEndingComboBox.SelectedItem = SendSettingsModel.DEFAULT_LINE_ENDING;
    }

    private void ShowMessage(string title, string message, InfoBarSeverity severity)
    {
        MessageBar.Title = title;
        MessageBar.Message = message;
        MessageBar.Severity = severity;
        MessageBar.IsOpen = true;
    }

    private System.Text.Encoding GetSelectedTextEncoding() =>
        ViewModel.Transmission.GetEncoding(EncodingComboBox.SelectedItem as string);

}
