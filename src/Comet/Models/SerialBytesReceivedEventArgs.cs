namespace Comet.Models;

public sealed class SerialBytesReceivedEventArgs(byte[] data, DateTime receivedAt) : EventArgs
{
    public byte[] Data { get; } = data;
    public DateTime ReceivedAt { get; } = receivedAt;
}
