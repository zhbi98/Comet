namespace Comet.Models;

/// <summary>
/// Carries an owned snapshot of one serial read and the time at which it was received.
/// </summary>
public sealed class SerialBytesReceivedEventArgs(byte[] data, DateTime receivedAt) : EventArgs
{
    public byte[] Data { get; } = data;
    public DateTime ReceivedAt { get; } = receivedAt;
}
