namespace Comet.Models;

public sealed class SerialBytesReceivedEventArgs(byte[] data) : EventArgs
{
    public byte[] Data { get; } = data;
}
