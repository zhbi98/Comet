using Comet.Models;

namespace Comet.Services.Abstractions;

/// <summary>
/// Defines the serial transport boundary consumed by the view models. Implementations
/// own the physical port and must copy received bytes before publishing them.
/// </summary>
public interface ISerialPortService : IDisposable
{
    /// <summary>
    /// Raised on the transport callback thread; subscribers must marshal UI work.
    /// </summary>
    event EventHandler<SerialBytesReceivedEventArgs>? BytesReceived;

    /// <summary>
    /// Reports asynchronous read failures on the originating worker thread.
    /// </summary>
    event Action<string>? ErrorOccurred;

    bool IsOpen { get; }

    string? PortName { get; }

    IReadOnlyList<SerialPortInfoModel> GetAvailablePorts();

    void Open(SerialPortConnectionOptions options);

    void Close();

    void Send(byte[] data);
}
