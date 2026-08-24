using Comet.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Comet;

public sealed partial class MainPage
{
    private void ClearTerminalButton_Click(object sender, RoutedEventArgs e)
    {
        _terminalRenderTimer.Stop();
        _terminalRenderPending = false;
        _pendingTerminalRemoveCount = 0;
        _pendingTerminalAppend.Clear();
        _terminalBuffer.Clear();
        SetTerminalText(string.Empty, preserveSelection: false);
        _receivedBytes = 0;
        _sentBytes = 0;
        EmptyTerminalPanel.Visibility = Visibility.Visible;
        UpdateTerminalItemStatus();
        UpdateCounters();
    }

    private void ReceiveHexCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _terminalRenderTimer.Stop();
        _terminalRenderPending = false;
        _pendingTerminalRemoveCount = 0;
        _pendingTerminalAppend.Clear();
        _terminalBuffer.SetReceiveAsHex(ReceiveHexCheckBox.IsChecked == true);
        var preserveSelection = TerminalTextBox.SelectionLength > 0 || AutoScrollCheckBox.IsChecked != true;
        SetTerminalText(_terminalBuffer.GetText(), preserveSelection);
        EmptyTerminalPanel.Visibility = _terminalBuffer.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        UpdateTerminalItemStatus();
    }

    private void TerminalTextBox_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        if (_isUpdatingTerminalText)
        {
            return;
        }

        var insertedText = GetInsertedText(sender.Text, args.NewText);
        args.Cancel = true;
        if (insertedText.Length > 0)
        {
            SendTerminalInput(insertedText);
        }
    }

    private static string GetInsertedText(string previousText, string currentText)
    {
        var prefixLength = 0;
        var sharedLength = Math.Min(previousText.Length, currentText.Length);
        while (prefixLength < sharedLength && previousText[prefixLength] == currentText[prefixLength])
        {
            prefixLength++;
        }

        var suffixLength = 0;
        while (suffixLength < previousText.Length - prefixLength &&
               suffixLength < currentText.Length - prefixLength &&
               previousText[^(suffixLength + 1)] == currentText[^(suffixLength + 1)])
        {
            suffixLength++;
        }

        var insertedLength = currentText.Length - prefixLength - suffixLength;
        return insertedLength > 0
            ? currentText.Substring(prefixLength, insertedLength)
            : string.Empty;
    }

    private void SendTerminalInput(string text)
    {
        if (!_serialPort.IsOpen)
        {
            return;
        }

        try
        {
            var configuredLineEnding = GetLineEnding(LineEndingComboBox.SelectedItem as string);
            var terminalLineEnding = configuredLineEnding.Length == 0 ? "\n" : configuredLineEnding;
            var terminalText = text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Replace("\n", terminalLineEnding);
            var payload = GetSelectedEncoding().GetBytes(terminalText);
            _serialPort.Send(payload);
            _sentBytes += payload.Length;
            UpdateCounters();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            ShowMessage("键入发送失败", exception.Message, InfoBarSeverity.Error);
            UpdateConnectionUi();
        }
    }

    private async void SaveLogButton_Click(object sender, RoutedEventArgs e)
    {
        var logText = _terminalBuffer.GetText();
        if (string.IsNullOrEmpty(logText))
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

            await FileIO.WriteTextAsync(file, logText, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            ShowMessage("日志已保存", file.Path, InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowMessage("保存失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private void AppendEntry(string direction, string text, bool isHex = false, byte[]? rawBytes = null)
    {
        var showDetails = TimestampCheckBox.IsChecked == true;
        var entry = new TerminalEntry
        {
            Time = DateTime.Now.ToString("HH:mm:ss.fff"),
            Direction = direction,
            Text = text,
            IsDetailed = showDetails,
            IsHex = isHex,
            RawBytes = rawBytes
        };

        var shouldDisplay = showDetails || direction == "RX";
        var update = _terminalBuffer.Append(entry, shouldDisplay, ReceiveHexCheckBox.IsChecked == true);
        if (!update.HasChange)
        {
            return;
        }

        var remainingRemoval = update.RemovedPrefixLength;
        if (remainingRemoval > 0)
        {
            var visibleAvailable = Math.Max(0, TerminalTextBox.Text.Length - _pendingTerminalRemoveCount);
            var removeFromVisible = Math.Min(remainingRemoval, visibleAvailable);
            _pendingTerminalRemoveCount += removeFromVisible;
            remainingRemoval -= removeFromVisible;

            if (remainingRemoval > 0 && _pendingTerminalAppend.Length > 0)
            {
                var removeFromPending = Math.Min(remainingRemoval, _pendingTerminalAppend.Length);
                _pendingTerminalAppend.Remove(0, removeFromPending);
                remainingRemoval -= removeFromPending;
            }
        }

        if (remainingRemoval < update.AppendedText.Length)
        {
            _pendingTerminalAppend.Append(update.AppendedText, remainingRemoval, update.AppendedText.Length - remainingRemoval);
        }

        ScheduleTerminalRender();
        EmptyTerminalPanel.Visibility = _terminalBuffer.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScheduleTerminalRender()
    {
        if (_terminalRenderPending)
        {
            return;
        }

        _terminalRenderTimer.Interval = TimeSpan.FromMilliseconds(_terminalBuffer.CurrentLength switch
        {
            < 25_000 => 50,
            < 75_000 => 100,
            _ => 250
        });
        _terminalRenderPending = true;
        _terminalRenderTimer.Start();
    }

    private void TerminalRenderTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        _terminalRenderPending = false;
        if (_isUnloaded)
        {
            _pendingTerminalRemoveCount = 0;
            _pendingTerminalAppend.Clear();
            return;
        }

        UpdateCounters();
        var preserveSelection = TerminalTextBox.SelectionLength > 0 || AutoScrollCheckBox.IsChecked != true;
        var removedPrefixLength = _pendingTerminalRemoveCount;
        _pendingTerminalRemoveCount = 0;
        var appendedText = _pendingTerminalAppend.ToString();
        _pendingTerminalAppend.Clear();
        if (removedPrefixLength > 0 || appendedText.Length > 0)
        {
            ApplyTerminalDelta(removedPrefixLength, appendedText, preserveSelection);
        }

        UpdateTerminalItemStatus();
    }

    private void UpdateTerminalItemStatus() =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(
            TerminalTextBox,
            $"可见 {_terminalBuffer.CurrentLength:N0} / {MaxTerminalCharacters:N0} 字符");

    private void SetTerminalText(string text, bool preserveSelection)
    {
        var selectionStart = TerminalTextBox.SelectionStart;
        var selectionLength = TerminalTextBox.SelectionLength;
        _isUpdatingTerminalText = true;
        try
        {
            TerminalTextBox.Text = text;
            if (preserveSelection)
            {
                var safeStart = Math.Min(selectionStart, text.Length);
                var safeLength = Math.Min(selectionLength, text.Length - safeStart);
                TerminalTextBox.Select(safeStart, safeLength);
            }
            else
            {
                TerminalTextBox.Select(text.Length, 0);
            }
        }
        finally
        {
            _isUpdatingTerminalText = false;
        }

        if (!preserveSelection)
        {
            QueueScrollTerminalToEnd();
        }
    }

    private void ApplyTerminalDelta(int removedPrefixLength, string appendedText, bool preserveSelection)
    {
        var selectionStart = TerminalTextBox.SelectionStart;
        var selectionLength = TerminalTextBox.SelectionLength;
        var restoreReadOnly = TerminalTextBox.IsReadOnly;
        _isUpdatingTerminalText = true;
        try
        {
            // SelectedText is the only incremental mutation API exposed by the
            // WinUI TextBox, but it throws UnauthorizedAccessException while the
            // control is read-only. A render queued just before disconnect can run
            // after UpdateConnectionUi marks the terminal read-only, so temporarily
            // allow this programmatic mutation. The UI thread cannot process user
            // input until this synchronous block has restored the original state.
            if (restoreReadOnly)
            {
                TerminalTextBox.IsReadOnly = false;
            }

            var safeRemoveCount = Math.Min(removedPrefixLength, TerminalTextBox.Text.Length);
            if (safeRemoveCount > 0)
            {
                TerminalTextBox.Select(0, safeRemoveCount);
                TerminalTextBox.SelectedText = string.Empty;
            }

            TerminalTextBox.Select(TerminalTextBox.Text.Length, 0);
            if (appendedText.Length > 0)
            {
                TerminalTextBox.SelectedText = appendedText;
            }

            if (preserveSelection)
            {
                var textLength = TerminalTextBox.Text.Length;
                var originalSelectionEnd = selectionStart + selectionLength;
                var safeStart = Math.Min(Math.Max(0, selectionStart - safeRemoveCount), textLength);
                var safeEnd = Math.Min(Math.Max(0, originalSelectionEnd - safeRemoveCount), textLength);
                var safeLength = Math.Max(0, safeEnd - safeStart);
                TerminalTextBox.Select(safeStart, safeLength);
            }
            else
            {
                TerminalTextBox.Select(TerminalTextBox.Text.Length, 0);
            }
        }
        finally
        {
            if (restoreReadOnly)
            {
                TerminalTextBox.IsReadOnly = true;
            }

            _isUpdatingTerminalText = false;
        }

        if (!preserveSelection)
        {
            QueueScrollTerminalToEnd();
        }
    }

    private void QueueScrollTerminalToEnd()
    {
        if (_terminalScrollPending)
        {
            return;
        }

        _terminalScrollPending = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _terminalScrollPending = false;
            if (!_isUnloaded)
            {
                ScrollTerminalToEnd();
            }
        });
    }

    private void ScrollTerminalToEnd()
    {
        var scrollViewer = FindVisualDescendant<ScrollViewer>(TerminalTextBox);
        scrollViewer?.ChangeView(
            0,
            scrollViewer.ScrollableHeight,
            null,
            disableAnimation: true);
    }

    private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
