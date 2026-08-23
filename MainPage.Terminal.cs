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
        _terminalBuffer.Clear();
        SetTerminalText(string.Empty, preserveSelection: false);
        _receivedBytes = 0;
        _sentBytes = 0;
        EmptyTerminalPanel.Visibility = Visibility.Visible;
        UpdateCounters();
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

    private void AppendEntry(string direction, string text, bool isHex = false)
    {
        var showDetails = TimestampCheckBox.IsChecked == true;
        var entry = new TerminalEntry
        {
            Time = DateTime.Now.ToString("HH:mm:ss.fff"),
            Direction = direction,
            Text = text,
            IsDetailed = showDetails,
            IsHex = isHex
        };

        var shouldDisplay = showDetails || direction == "RX";
        if (!_terminalBuffer.Append(entry, shouldDisplay))
        {
            return;
        }

        ScheduleTerminalRender();
        EmptyTerminalPanel.Visibility = Visibility.Collapsed;
    }

    private void ScheduleTerminalRender()
    {
        if (_terminalRenderPending)
        {
            return;
        }

        _terminalRenderPending = true;
        _terminalRenderTimer.Start();
    }

    private void TerminalRenderTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        _terminalRenderPending = false;
        var preserveSelection = TerminalTextBox.SelectionLength > 0 || AutoScrollCheckBox.IsChecked != true;
        SetTerminalText(_terminalBuffer.GetText(), preserveSelection);
    }

    private void SetTerminalText(string text, bool preserveSelection)
    {
        var selectionStart = TerminalTextBox.SelectionStart;
        var selectionLength = TerminalTextBox.SelectionLength;
        _displayedTerminalText = text;
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
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ScrollTerminalToEnd);
        }
    }

    private void ScrollTerminalToEnd()
    {
        TerminalTextBox.UpdateLayout();
        var scrollViewer = FindVisualDescendant<ScrollViewer>(TerminalTextBox);
        scrollViewer?.ChangeView(null, scrollViewer.ScrollableHeight, null, disableAnimation: true);
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
