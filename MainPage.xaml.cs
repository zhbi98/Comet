using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Comet.Features.Terminal;
using Comet.Models;
using Comet.Services;
using Comet.Utilities;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Comet;

public sealed partial class MainPage : Page
{
    private const int MaxTerminalEntries = 3000;
    private const int MaxTerminalCharacters = 1_000_000;

    private readonly SerialPortService _serialPort = new();
    private readonly TerminalBuffer _terminalBuffer;
    private readonly ConcurrentQueue<byte[]> _receiveQueue;
    private readonly ObservableCollection<CommandPreset> _commandPresets = [];
    private readonly DispatcherQueueTimer _repeatTimer;
    private readonly DispatcherQueueTimer _terminalRenderTimer;
    private readonly SolidColorBrush _connectedBrush = new(Windows.UI.Color.FromArgb(255, 22, 135, 93));
    private readonly SolidColorBrush _disconnectedBrush = new(Windows.UI.Color.FromArgb(255, 102, 118, 138));

    private long _receivedBytes;
    private long _sentBytes;
    private int _receiveDispatchScheduled;
    private string _displayedTerminalText = string.Empty;
    private bool _isUpdatingTerminalText;
    private bool _terminalRenderPending;
    private bool _isUnloaded;

    public MainPage()
    {
        InitializeComponent();
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        _terminalBuffer = new TerminalBuffer(MaxTerminalEntries, MaxTerminalCharacters);
        _receiveQueue = new ConcurrentQueue<byte[]>();

        _repeatTimer = DispatcherQueue.CreateTimer();
        _repeatTimer.Interval = TimeSpan.FromSeconds(1);
        _repeatTimer.Tick += RepeatTimer_Tick;

        _terminalRenderTimer = DispatcherQueue.CreateTimer();
        _terminalRenderTimer.Interval = TimeSpan.FromMilliseconds(33);
        _terminalRenderTimer.IsRepeating = false;
        _terminalRenderTimer.Tick += TerminalRenderTimer_Tick;

        TerminalTextBox.BeforeTextChanging += TerminalTextBox_BeforeTextChanging;
        _serialPort.BytesReceived += SerialPort_BytesReceived;
        _serialPort.ErrorOccurred += SerialPort_ErrorOccurred;

        InitializeOptions();
        InitializeCommandPresets();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = false;
        RefreshPorts();
        UpdateConnectionUi();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        _repeatTimer.Stop();
        _terminalRenderTimer.Stop();
        _serialPort.Dispose();
    }

    private void InitializeOptions()
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

    private System.Text.Encoding GetSelectedEncoding() =>
        TextEncodingCatalog.Get(EncodingComboBox.SelectedItem as string);

    private static string GetLineEnding(string? lineEnding) => lineEnding switch
    {
        "CRLF" => "\r\n",
        "CR" => "\r",
        "LF" => "\n",
        _ => string.Empty
    };

    private static string FormatByteCount(long value) => value switch
    {
        >= 1024 * 1024 => $"{value / 1024d / 1024d:F2} MB",
        >= 1024 => $"{value / 1024d:F2} KB",
        _ => $"{value} B"
    };
}
