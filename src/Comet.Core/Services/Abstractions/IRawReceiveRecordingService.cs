using Comet.Models;

namespace Comet.Services.Abstractions;

/// <summary>
/// Persists immutable serial receive buffers without applying text decoding or
/// terminal formatting.
/// </summary>
public interface IRawReceiveRecordingService : IDisposable
{
    event EventHandler? StateChanged;

    event EventHandler<RawReceiveRecordingFailedEventArgs>? RecordingFailed;

    bool IsRecording { get; }

    string? FilePath { get; }

    void Start(string filePath);

    bool TryWrite(byte[] data);

    Task StopAsync();
}
