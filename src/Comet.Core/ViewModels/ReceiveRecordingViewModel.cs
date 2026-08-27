using Comet.Models;
using Comet.Services.Abstractions;

namespace Comet.ViewModels;

/// <summary>
/// Exposes raw receive recording state without coupling file writes to WinUI.
/// </summary>
public sealed class ReceiveRecordingViewModel : IDisposable
{
    private readonly IRawReceiveRecordingService _recordingService;

    public ReceiveRecordingViewModel(IRawReceiveRecordingService recordingService)
    {
        _recordingService = recordingService;
        _recordingService.StateChanged += RecordingService_StateChanged;
        _recordingService.RecordingFailed += RecordingService_RecordingFailed;
    }

    public event EventHandler? StateChanged;

    public event EventHandler<RawReceiveRecordingFailedEventArgs>? RecordingFailed;

    public bool IsRecording => _recordingService.IsRecording;

    public string? FilePath => _recordingService.FilePath;

    public string ButtonText => IsRecording ? "停止录制" : "数据录制";

    public void Start(string filePath) => _recordingService.Start(filePath);

    public bool TryRecord(byte[] data) => _recordingService.TryWrite(data);

    public Task StopAsync() => _recordingService.StopAsync();

    private void RecordingService_StateChanged(object? sender, EventArgs args) =>
        StateChanged?.Invoke(this, EventArgs.Empty);

    private void RecordingService_RecordingFailed(
        object? sender,
        RawReceiveRecordingFailedEventArgs args) => RecordingFailed?.Invoke(this, args);

    public void Dispose()
    {
        _recordingService.StateChanged -= RecordingService_StateChanged;
        _recordingService.RecordingFailed -= RecordingService_RecordingFailed;
        _recordingService.Dispose();
    }
}
