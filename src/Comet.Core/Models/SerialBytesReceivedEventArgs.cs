using System.Diagnostics;

namespace Comet.Models;

/// <summary>
/// Carries an owned snapshot of one serial read, its display time, and its monotonic timestamp.
/// </summary>
public sealed class SerialBytesReceivedEventArgs(byte[] data, DateTime receivedAt) : EventArgs
{
    public byte[] Data { get; } = data;

    /// <summary>Wall-clock time used for terminal display.</summary>
    public DateTime ReceivedAt { get; } = receivedAt;

    /// <summary>Monotonic Stopwatch timestamp used only for elapsed-time measurement.</summary>
    public long ReceivedTimestamp { get; } = Stopwatch.GetTimestamp();
}
