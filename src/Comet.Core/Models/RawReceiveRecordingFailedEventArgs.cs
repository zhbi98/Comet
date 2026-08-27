namespace Comet.Models;

public sealed class RawReceiveRecordingFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
