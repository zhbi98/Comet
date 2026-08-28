using System.IO.Ports;
using System.Runtime.ExceptionServices;
using Comet.Models;
using Comet.Services.Abstractions;
using Comet.Services.Serial;

namespace Comet.Services;

public sealed class SerialPortService : ISerialPortService
{
    private static readonly TimeSpan _connectionMonitorInterval = TimeSpan.FromMilliseconds(500);

    private readonly object _syncRoot = new();
    private CancellationTokenSource? _connectionMonitorCancellation;
    private SerialPortConnectionOptions? _connectionOptions;
    private long _connectionGeneration;
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

    public bool IsConnectionActive
    {
        get
        {
            lock (_syncRoot)
            {
                return _connectionOptions is not null;
            }
        }
    }

    public string? PortName
    {
        get
        {
            lock (_syncRoot)
            {
                return _connectionOptions?.PortName ?? _port?.PortName;
            }
        }
    }

    public IReadOnlyList<SerialPortInfoModel> GetAvailablePorts() =>
        SerialPortDiscovery.GetAvailablePorts();

    public void Open(SerialPortConnectionOptions options)
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _connectionGeneration++;
            CancelConnectionMonitorCore();
            CloseCore();
            _connectionOptions = null;
            var port = SerialPortFactory.Create(options, OnDataReceived);
            try
            {
                port.Open();
                _port = port;
                _connectionOptions = options;
                StartConnectionMonitorCore();
            }
            catch
            {
                DisposePort(port);
                throw;
            }
        }
    }

    public void Close()
    {
        lock (_syncRoot)
        {
            _connectionGeneration++;
            CancelConnectionMonitorCore();
            _connectionOptions = null;
            CloseCore();
        }
    }

    public void Send(byte[] data)
    {
        if (data.Length == 0)
        {
            return;
        }

        Exception? failure = null;
        lock (_syncRoot)
        {
            var port = _port;
            if (port?.IsOpen != true)
            {
                throw new InvalidOperationException("串口尚未连接。");
            }

            try
            {
                port.Write(data, 0, data.Length);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                failure = exception;
                TryBeginRecoveryCore(port);
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = (SerialPort)sender;
        if (!ReferenceEquals(Volatile.Read(ref _port), port))
        {
            return;
        }

        byte[]? receivedData = null;
        try
        {
            // Copy bytes before raising the event: the SerialPort buffer and callback
            // thread must not leak into consumers that process data asynchronously.
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

            receivedData = buffer;
        }
        catch (TimeoutException exception)
        {
            ErrorOccurred?.Invoke(exception.Message);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            HandleTransportFailure(port);
        }

        if (receivedData is not null)
        {
            // Subscriber failures are outside the transport exception boundary and
            // must never cause a healthy serial handle to enter recovery.
            BytesReceived?.Invoke(this, new SerialBytesReceivedEventArgs(receivedData, DateTime.Now));
        }
    }

    private void CloseCore()
    {
        // Clear the shared reference before closing. Concurrent Send calls then fail
        // deterministically instead of writing through a port being disposed.
        var port = _port;
        _port = null;
        if (port is null)
        {
            return;
        }

        DisposePort(port);
    }

    private void DisposePort(SerialPort port)
    {
        port.DataReceived -= OnDataReceived;
        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // A removed USB serial device can invalidate its native handle before
            // SerialPort observes that the port is closed. Disposal must still finish.
        }
        finally
        {
            try
            {
                port.Dispose();
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                // The operating-system handle is already unusable; there is no
                // remaining managed resource that can be recovered here.
            }
        }
    }

    private bool TryBeginRecoveryCore(SerialPort port)
    {
        if (_disposed ||
            _connectionOptions is null ||
            !ReferenceEquals(_port, port))
        {
            return false;
        }

        CloseCore();
        return true;
    }

    private void HandleTransportFailure(SerialPort port)
    {
        lock (_syncRoot)
        {
            // A callback can finish after Close has detached its sender. Such stale
            // failures belong to the retired handle and must not reach the page.
            TryBeginRecoveryCore(port);
        }
    }

    private void StartConnectionMonitorCore()
    {
        var cancellation = new CancellationTokenSource();
        var generation = _connectionGeneration;
        _connectionMonitorCancellation = cancellation;
        _ = Task.Run(() => MonitorConnectionAsync(generation, cancellation));
    }

    private void CancelConnectionMonitorCore()
    {
        var cancellation = _connectionMonitorCancellation;
        _connectionMonitorCancellation = null;
        cancellation?.Cancel();
    }

    private async Task MonitorConnectionAsync(
        long generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_connectionMonitorInterval, cancellation.Token).ConfigureAwait(false);
                MonitorConnection(generation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void MonitorConnection(long generation)
    {
        lock (_syncRoot)
        {
            if (_disposed ||
                generation != _connectionGeneration ||
                _connectionOptions is not { } options)
            {
                return;
            }

            var isPortPresent = SerialPortDiscovery.IsPortPresent(options.PortName);
            if (_port is { } currentPort)
            {
                if (currentPort.IsOpen && isPortPresent)
                {
                    return;
                }

                CloseCore();
            }

            if (isPortPresent)
            {
                var port = SerialPortFactory.Create(options, OnDataReceived);
                try
                {
                    port.Open();
                    _port = port;
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    DisposePort(port);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _connectionGeneration++;
            CancelConnectionMonitorCore();
            _connectionOptions = null;
            CloseCore();
            _disposed = true;
        }
    }
}
