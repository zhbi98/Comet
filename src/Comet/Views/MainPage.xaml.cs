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
    private readonly ConcurrentQueue<SerialBytesReceivedEventArgs> _receiveQueue;
    private readonly DispatcherQueueTimer _terminalRenderTimer;
    private readonly SolidColorBrush _connectedBrush = new(Windows.UI.Color.FromArgb(255, 22, 135, 93));
    private readonly SolidColorBrush _disconnectedBrush = new(Windows.UI.Color.FromArgb(255, 102, 118, 138));

    private int _receiveDrainScheduled;
    private readonly StringBuilder _pendingTerminalText = new();
    private bool _isTerminalRenderPending;
    private bool _isUnloaded;
    private int _shutdownState;

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
        SendHexCheckBox.Click += (_, _) => UpdateRepeatSendPayload();
        LineEndingComboBox.SelectionChanged += (_, _) => UpdateRepeatSendPayload();

        _terminalRenderTimer = DispatcherQueue.CreateTimer();
        _terminalRenderTimer.Interval = TimeSpan.FromMilliseconds(50);
        _terminalRenderTimer.IsRepeating = false;
        _terminalRenderTimer.Tick += TerminalRenderTimer_Tick;

        TerminalView.InputReceived += (_, args) => SendTerminalInput(args.Text);
        TerminalView.ViewportChanged += (_, _) => UpdateTerminalItemStatus();
        ViewModel.TerminalAppearance.PropertyChanged += TerminalAppearance_PropertyChanged;
        ViewModel.CommandPresets.PropertyChanged += CommandPresets_PropertyChanged;
        ApplyTerminalAppearance();
        AutoScrollCheckBox.Click += (_, _) =>
        {
            TerminalView.AutoScroll = AutoScrollCheckBox.IsChecked == true;
            if (TerminalView.AutoScroll)
            {
                TerminalView.ScrollToEnd();
            }
        };
        ViewModel.Connection.BytesReceived += SerialPort_BytesReceived;
        ViewModel.Connection.ErrorOccurred += SerialPort_ErrorOccurred;
        ViewModel.RepeatSending.PayloadSent += RepeatSending_PayloadSent;
        ViewModel.RepeatSending.SendFailed += RepeatSending_SendFailed;

        InitializeSerialOptions();
        InitializeCommandPresets();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = false;
        RefreshPorts();
        UpdateConnectionState();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        Shutdown();
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
        ViewModel.TerminalAppearance.PropertyChanged -= TerminalAppearance_PropertyChanged;
        ViewModel.CommandPresets.PropertyChanged -= CommandPresets_PropertyChanged;
        StopRepeatSending();
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
        BaudRateComboBox.SelectedItem = 115200;
        DataBitsComboBox.ItemsSource = new[] { 5, 6, 7, 8 };
        DataBitsComboBox.SelectedItem = 8;
        StopBitsComboBox.ItemsSource = new[] { "1", "1.5", "2" };
        StopBitsComboBox.SelectedIndex = 0;
        ParityComboBox.ItemsSource = new[] { "None", "Odd", "Even", "Mark", "Space" };
        ParityComboBox.SelectedIndex = 0;
        HandshakeComboBox.ItemsSource = new[] { "None", "XOn/XOff", "RTS/CTS", "RTS/CTS + XOn/XOff" };
        HandshakeComboBox.SelectedIndex = 0;
        EncodingComboBox.ItemsSource = new[] { "UTF-8", "GBK", "ASCII" };
        EncodingComboBox.SelectedIndex = 0;
        LineEndingComboBox.ItemsSource = new[] { "无", "CRLF", "CR", "LF" };
        LineEndingComboBox.SelectedIndex = 0;
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
