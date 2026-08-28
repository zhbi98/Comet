using System.Collections.ObjectModel;
using Comet.Models;
using Comet.Services.Abstractions;

namespace Comet.ViewModels;

/// <summary>
/// Owns serial connection state while leaving control layout and notifications to the view.
/// </summary>
public sealed class ConnectionViewModel : ObservableObject, IDisposable
{
    private readonly ISerialPortService _serialPortService;
    private SerialPortInfoModel? _selectedPort;
    private string _portHint = "请选择可用串口后连接";

    public ConnectionViewModel(ISerialPortService serialPortService)
    {
        _serialPortService = serialPortService;
        // Re-publish transport events so views depend on the view model rather than
        // reaching through it to the concrete serial service.
        _serialPortService.BytesReceived += SerialPortService_BytesReceived;
        _serialPortService.ErrorOccurred += SerialPortService_ErrorOccurred;
    }

    public event EventHandler<SerialBytesReceivedEventArgs>? BytesReceived;

    public event Action<string>? ErrorOccurred;

    public ObservableCollection<SerialPortInfoModel> Ports { get; } = [];

    public SerialPortInfoModel? SelectedPort
    {
        get => _selectedPort;
        set => SetProperty(ref _selectedPort, value);
    }

    public string PortHint
    {
        get => _portHint;
        private set => SetProperty(ref _portHint, value);
    }

    public bool IsConnected => _serialPortService.IsOpen;

    public bool IsConnectionActive => _serialPortService.IsConnectionActive;

    public string? PortName => _serialPortService.PortName;

    public void RefreshPorts()
    {
        // Preserve selection by COM name because each enumeration creates new model
        // instances and the previously selected object is no longer in the collection.
        var previousPortName = SelectedPort?.PortName;
        var ports = _serialPortService.GetAvailablePorts();

        Ports.Clear();
        foreach (var port in ports)
        {
            Ports.Add(port);
        }

        SelectedPort = Ports.FirstOrDefault(port =>
                           string.Equals(port.PortName, previousPortName, StringComparison.OrdinalIgnoreCase)) ??
                       Ports.FirstOrDefault();
        PortHint = Ports.Count == 0
            ? "未发现串口，请检查设备驱动或 USB 连接。"
            : $"发现 {Ports.Count} 个串口：{string.Join("、", Ports.Select(port => port.DisplayName))}";
    }

    public void Open(SerialPortConnectionOptions options)
    {
        _serialPortService.Open(options);
        PortHint = "参数已锁定，断开后可修改。";
        NotifyConnectionPropertiesChanged();
    }

    public string Close()
    {
        // Capture the name before Close clears the service state; the caller uses it
        // for the existing SYS disconnect entry.
        var portName = PortName ?? "串口";
        _serialPortService.Close();
        NotifyConnectionPropertiesChanged();
        return portName;
    }

    public void Send(byte[] data) => _serialPortService.Send(data);

    private void NotifyConnectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsConnectionActive));
        OnPropertyChanged(nameof(PortName));
    }

    private void SerialPortService_BytesReceived(object? sender, SerialBytesReceivedEventArgs args) =>
        BytesReceived?.Invoke(this, args);

    private void SerialPortService_ErrorOccurred(string message) => ErrorOccurred?.Invoke(message);

    public void Dispose()
    {
        _serialPortService.BytesReceived -= SerialPortService_BytesReceived;
        _serialPortService.ErrorOccurred -= SerialPortService_ErrorOccurred;
        _serialPortService.Dispose();
    }
}
