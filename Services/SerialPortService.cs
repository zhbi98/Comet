using System.IO.Ports;

namespace Comet.Services;

public sealed record SerialPortSettings(
    string PortName,
    int BaudRate,
    int DataBits,
    Parity Parity,
    StopBits StopBits,
    Handshake Handshake,
    bool DtrEnable,
    bool RtsEnable);

public sealed class SerialBytesReceivedEventArgs(byte[] data) : EventArgs
{
    public byte[] Data { get; } = data;
}

public sealed class SerialPortService : IDisposable
{
    private readonly object _syncRoot = new();
    private SerialPort? _port;
    private bool _disposed;

    public event EventHandler<SerialBytesReceivedEventArgs>? BytesReceived;
    public event Action<string>? ErrorOccurred;

    public bool IsOpen
    {
        get
        {
            lock (_syncRoot)
            {
                return _port?.IsOpen == true;
            }
        }
    }

    public string? PortName
    {
        get
        {
            lock (_syncRoot)
            {
                return _port?.PortName;
            }
        }
    }

    public static IReadOnlyList<string> GetAvailablePorts()
    {
        return SerialPort.GetPortNames()
            .OrderBy(GetPortNumber)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Open(SerialPortSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_syncRoot)
        {
            CloseCore();
            var port = new SerialPort
            {
                PortName = settings.PortName,
                BaudRate = settings.BaudRate,
                DataBits = settings.DataBits,
                Parity = settings.Parity,
                StopBits = settings.StopBits,
                Handshake = settings.Handshake,
                DtrEnable = settings.DtrEnable,
                RtsEnable = settings.RtsEnable,
                ReadTimeout = 500,
                WriteTimeout = 1000,
                ReadBufferSize = 16 * 1024,
                WriteBufferSize = 4 * 1024
            };

            port.DataReceived += OnDataReceived;
            try
            {
                port.Open();
                _port = port;
            }
            catch
            {
                port.DataReceived -= OnDataReceived;
                port.Dispose();
                throw;
            }
        }
    }

    public void Close()
    {
        lock (_syncRoot)
        {
            CloseCore();
        }
    }

    public void Send(byte[] data)
    {
        if (data.Length == 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_port?.IsOpen != true)
            {
                throw new InvalidOperationException("串口尚未连接。");
            }

            _port.Write(data, 0, data.Length);
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var port = (SerialPort)sender;
            var count = port.BytesToRead;
            if (count <= 0)
            {
                return;
            }

            var buffer = new byte[count];
            var read = port.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                return;
            }

            if (read != buffer.Length)
            {
                Array.Resize(ref buffer, read);
            }

            BytesReceived?.Invoke(this, new SerialBytesReceivedEventArgs(buffer));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            ErrorOccurred?.Invoke(exception.Message);
        }
    }

    private void CloseCore()
    {
        var port = _port;
        _port = null;
        if (port is null)
        {
            return;
        }

        port.DataReceived -= OnDataReceived;
        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
        }
        finally
        {
            port.Dispose();
        }
    }

    private static int GetPortNumber(string portName)
    {
        var digits = new string(portName.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : int.MaxValue;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_syncRoot)
        {
            CloseCore();
            _disposed = true;
        }
    }
}
