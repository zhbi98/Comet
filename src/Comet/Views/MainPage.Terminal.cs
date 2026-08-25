using Comet.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Comet.Views;

public sealed partial class MainPage
{
    private void ClearTerminalButton_Click(object sender, RoutedEventArgs e)
    {
        _terminalRenderTimer.Stop();
        _isTerminalRenderPending = false;
        _pendingTerminalText.Clear();
        ViewModel.Terminal.Clear();
        TerminalView.Clear();
        EmptyTerminalPanel.Visibility = Visibility.Visible;
        UpdateTerminalItemStatus();
        UpdateTransferCounters();
    }

    private void ReceiveHexCheckBox_Click(object sender, RoutedEventArgs e)
    {
        // TerminalBuffer already maintains both formatted sessions. Discarding the
        // pending UI suffix is safe because SetText reads the complete selected view.
        _terminalRenderTimer.Stop();
        _isTerminalRenderPending = false;
        _pendingTerminalText.Clear();
        ViewModel.Terminal.SetReceiveAsHex(ReceiveHexCheckBox.IsChecked == true);
        TerminalView.SetText(
            ViewModel.Terminal.SessionText,
            AutoScrollCheckBox.IsChecked == true);
        EmptyTerminalPanel.Visibility = ViewModel.Terminal.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        UpdateTerminalItemStatus();
    }

    private void SendTerminalInput(string text)
    {
        if (!ViewModel.Connection.IsConnected)
        {
            return;
        }

        try
        {
            // The terminal input surface forwards real newline characters, unlike the
            // bottom composer where escape sequences are parsed before this point.
            var payload = ViewModel.Transmission.PrepareTerminalInput(
                text,
                LineEndingComboBox.SelectedItem as string,
                GetSelectedTextEncoding());
            ViewModel.Connection.Send(payload);
            ViewModel.Terminal.RecordSent(payload.Length);
            UpdateTransferCounters();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            ShowMessage("键入发送失败", exception.Message, InfoBarSeverity.Error);
            UpdateConnectionState();
        }
    }

    private async void SaveLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Terminal.SessionLength == 0)
        {
            ShowMessage("没有可保存的内容", "终端日志目前为空。", InfoBarSeverity.Informational);
            return;
        }

        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"Comet_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            picker.FileTypeChoices.Add("文本日志", [".txt"]);

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

            var logText = ViewModel.Terminal.SessionText;
            await FileIO.WriteTextAsync(file, logText, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            ShowMessage("日志已保存", file.Path, InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowMessage("保存失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private void AppendTerminalEntry(
        string direction,
        string text,
        bool isHex = false,
        byte[]? rawBytes = null,
        DateTime? timestamp = null)
    {
        var shouldShowDetails = TimestampCheckBox.IsChecked == true;
        var entry = new TerminalEntryModel
        {
            Time = (timestamp ?? DateTime.Now).ToString("HH:mm:ss.fff"),
            Direction = direction,
            Text = text,
            IsDetailed = shouldShowDetails,
            IsHex = isHex,
            RawBytes = rawBytes
        };

        var shouldDisplay = shouldShowDetails || direction == "RX";
        var update = ViewModel.Terminal.Append(entry, shouldDisplay, ReceiveHexCheckBox.IsChecked == true);
        if (!update.HasChange)
        {
            return;
        }

        // Coalesce small transport callbacks before notifying ItemsRepeater. This keeps
        // byte accounting immediate without forcing one layout pass per receive event.
        _pendingTerminalText.Append(update.AppendedText);
        ScheduleTerminalRender();
        EmptyTerminalPanel.Visibility = Visibility.Collapsed;
    }

    private void ScheduleTerminalRender()
    {
        if (_isTerminalRenderPending)
        {
            return;
        }

        // Larger bursts wait slightly longer so one render processes more data while
        // ordinary interactive traffic still appears with low latency.
        _terminalRenderTimer.Interval = TimeSpan.FromMilliseconds(_pendingTerminalText.Length switch
        {
            < 25_000 => 33,
            < 250_000 => 50,
            _ => 100
        });
        _isTerminalRenderPending = true;
        _terminalRenderTimer.Start();
    }

    private void TerminalRenderTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        _isTerminalRenderPending = false;
        if (_isUnloaded)
        {
            _pendingTerminalText.Clear();
            return;
        }

        UpdateTransferCounters();
        if (_pendingTerminalText.Length > 0)
        {
            var appendedText = _pendingTerminalText.ToString();
            _pendingTerminalText.Clear();
            TerminalView.AutoScroll = AutoScrollCheckBox.IsChecked == true;
            TerminalView.AppendText(appendedText);
        }

        UpdateTerminalItemStatus();
    }

    private void UpdateTerminalItemStatus()
    {
        string status;
        if (TerminalView.CharacterCount == 0)
        {
            status = "行 0 / 0 · 会话 0";
        }
        else
        {
            var (first, last) = TerminalView.GetVisibleLineRange();
            status = $"行 {first + 1:N0}-{last + 1:N0} / {TerminalView.LineCount:N0} · 会话 {ViewModel.Terminal.SessionLength:N0}";
        }

        TerminalBufferStatusText.Text = status;
        AutomationProperties.SetItemStatus(
            TerminalView,
            $"{status} 字符；内容区按可见行虚拟化显示完整会话");
    }
}
