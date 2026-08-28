using Comet.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Comet.Views;

public sealed partial class MainPage
{
    private bool _isReceiveRecordingStopping;

    private async void ReceiveRecordingButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ReceiveRecording.IsRecording)
        {
            await StopReceiveRecordingAsync(showConfirmation: true);
            return;
        }

        if (!ViewModel.Connection.IsConnected)
        {
            ShowMessage("无法开始录制", "请先连接串口。", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"Comet_{ViewModel.Connection.PortName}_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            picker.FileTypeChoices.Add("原始二进制数据", [".bin"]);

            var window = App.CurrentWindow;
            if (window is null)
            {
                return;
            }

            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            ViewModel.ReceiveRecording.Start(file.Path);
            UpdateReceiveRecordingState();
            ShowMessage("原始数据录制已开始", file.Path, InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowMessage("无法开始录制", exception.Message, InfoBarSeverity.Error);
            UpdateReceiveRecordingState();
        }
    }

    private async Task StopReceiveRecordingAsync(bool showConfirmation)
    {
        var filePath = ViewModel.ReceiveRecording.FilePath;
        _isReceiveRecordingStopping = true;
        ReceiveRecordingButton.IsEnabled = false;
        try
        {
            await ViewModel.ReceiveRecording.StopAsync();
            if (showConfirmation && filePath is not null)
            {
                ShowMessage("原始数据录制已停止", filePath, InfoBarSeverity.Success);
            }
        }
        catch (Exception exception)
        {
            ShowMessage("停止录制失败", exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _isReceiveRecordingStopping = false;
            UpdateReceiveRecordingState();
        }
    }

    private void ReceiveRecording_StateChanged(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isUnloaded)
            {
                UpdateReceiveRecordingState();
            }
        });
    }

    private void ReceiveRecording_RecordingFailed(
        object? sender,
        RawReceiveRecordingFailedEventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_isUnloaded)
            {
                return;
            }

            UpdateReceiveRecordingState();
            ShowMessage("原始数据录制已停止", e.Exception.Message, InfoBarSeverity.Error);
        });
    }

    private void UpdateReceiveRecordingState()
    {
        var recording = ViewModel.ReceiveRecording;
        ReceiveRecordingButton.Content = recording.ButtonText;
        ReceiveRecordingButton.IsEnabled =
            !_isReceiveRecordingStopping &&
            (recording.IsRecording || ViewModel.Connection.IsConnectionActive);
    }
}
