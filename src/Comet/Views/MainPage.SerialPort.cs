using System.Diagnostics;
using Comet.Converters;
using Comet.Helpers;
using Comet.Models;
using Comet.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Comet.Views;

public sealed partial class MainPage
{
    private sealed record PreparedSerialPayload(byte[] Bytes, string DisplayText, bool IsHex);

    private void RefreshPortsButton_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void RefreshPorts()
    {
        var previousPortName = (PortComboBox.SelectedItem as SerialPortInfoModel)?.PortName;
        var ports = SerialPortService.GetAvailablePorts();
        PortComboBox.ItemsSource = ports;

        var previous = ports.FirstOrDefault(port =>
            string.Equals(port.PortName, previousPortName, StringComparison.OrdinalIgnoreCase));
        if (previous is not null)
        {
            PortComboBox.SelectedItem = previous;
        }
        else if (ports.Count > 0)
        {
            PortComboBox.SelectedIndex = 0;
        }

        PortHintText.Text = ports.Count == 0
            ? "未发现串口，请检查设备驱动或 USB 连接。"
            : $"发现 {ports.Count} 个串口：{string.Join("、", ports.Select(port => port.DisplayName))}";
    }

    private void OpenCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_serialPortService.IsOpen)
        {
            DisconnectSerialPort();
            return;
        }

        if (PortComboBox.SelectedItem is not SerialPortInfoModel selectedPort)
        {
            ShowMessage("没有可用串口", "请连接设备并刷新串口列表。", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var portName = selectedPort.PortName;
            var connectionOptions = new SerialPortConnectionOptions(
                portName,
                (int)(BaudRateComboBox.SelectedItem ?? 115200),
                (int)(DataBitsComboBox.SelectedItem ?? 8),
                SerialPortSettingsConverter.ParseParity(ParityComboBox.SelectedItem as string),
                SerialPortSettingsConverter.ParseStopBits(StopBitsComboBox.SelectedItem as string),
                SerialPortSettingsConverter.ParseHandshake(HandshakeComboBox.SelectedItem as string),
                DtrToggle.IsOn,
                RtsToggle.IsOn);

            _serialPortService.Open(connectionOptions);
            _receiveTextDecoder.Reset();
            var parity = SerialPortSettingsConverter.GetParityShortName(connectionOptions.Parity);
            var stopBits = SerialPortSettingsConverter.GetStopBitsShortName(connectionOptions.StopBits);
            AppendTerminalEntry("SYS", $"已连接 {portName}  ·  {connectionOptions.BaudRate} / {connectionOptions.DataBits}{parity}{stopBits}");
            UpdateConnectionState();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or ArgumentException or InvalidOperationException)
        {
            ShowMessage("连接失败", exception.Message, InfoBarSeverity.Error);
            UpdateConnectionState();
        }
    }

    private void DisconnectSerialPort()
    {
        var portName = _serialPortService.PortName ?? "串口";
        StopRepeatSending();
        RepeatSendToggle.IsOn = false;
        _serialPortService.Close();
        _receiveTextDecoder.Reset();
        AppendTerminalEntry("SYS", $"{portName} 已断开");
        UpdateConnectionState();
        RefreshPorts();
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => SendComposerPayload(shouldShowErrors: true);

    private bool SendComposerPayload(bool shouldShowErrors) => SendPayload(
        SendTextBox.Text,
        SendHexCheckBox.IsChecked == true,
        LineEndingComboBox.SelectedItem as string,
        shouldShowErrors);

    private bool SendPayload(string content, bool isHex, string? lineEnding, bool shouldShowErrors)
    {
        if (!_serialPortService.IsOpen)
        {
            if (shouldShowErrors)
            {
                ShowMessage("无法发送", "请先连接串口。", InfoBarSeverity.Warning);
            }

            return false;
        }

        if (!TryPreparePayload(content, isHex, lineEnding, shouldShowErrors, out var payload))
        {
            return false;
        }

        try
        {
            var sentAt = DateTime.Now;
            _serialPortService.Send(payload.Bytes);
            _totalSentBytes += payload.Bytes.Length;
            AppendTerminalEntry("TX", payload.DisplayText, payload.IsHex, timestamp: sentAt);
            UpdateTransferCounters();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            if (shouldShowErrors)
            {
                ShowMessage("发送失败", exception.Message, InfoBarSeverity.Error);
            }

            return false;
        }
    }

    private bool TryPreparePayload(
        string content,
        bool isHex,
        string? lineEnding,
        bool shouldShowErrors,
        out PreparedSerialPayload payload)
    {
        byte[] bytes;
        string displayText;
        if (isHex)
        {
            if (!HexCodec.TryParse(content, out bytes, out var error))
            {
                if (shouldShowErrors)
                {
                    ShowMessage("HEX 格式错误", error, InfoBarSeverity.Warning);
                }

                payload = null!;
                return false;
            }

            displayText = HexCodec.Format(bytes);
        }
        else
        {
            if (!TextEscapeCodec.TryDecode(content, out var decodedText, out var escapeError))
            {
                if (shouldShowErrors)
                {
                    ShowMessage("转义格式错误", escapeError, InfoBarSeverity.Warning);
                }

                payload = null!;
                return false;
            }

            var text = decodedText + ResolveLineEnding(lineEnding);
            if (text.Length == 0)
            {
                if (shouldShowErrors)
                {
                    ShowMessage("没有发送内容", "请输入文本或选择一个行尾符。", InfoBarSeverity.Warning);
                }

                payload = null!;
                return false;
            }

            bytes = GetSelectedTextEncoding().GetBytes(text);
            displayText = text;
        }

        payload = new PreparedSerialPayload(bytes, displayText, isHex);
        return true;
    }

    private void SerialPort_BytesReceived(object? sender, SerialBytesReceivedEventArgs e)
    {
        _receiveQueue.Enqueue(e);
        ScheduleReceiveQueueDrain();
    }

    private void ScheduleReceiveQueueDrain()
    {
        if (Interlocked.Exchange(ref _receiveDrainScheduled, 1) != 0)
        {
            return;
        }

        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, DrainReceiveQueue))
        {
            Interlocked.Exchange(ref _receiveDrainScheduled, 0);
        }
    }

    private void DrainReceiveQueue()
    {
        if (_isUnloaded)
        {
            while (_receiveQueue.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref _receiveDrainScheduled, 0);
            return;
        }

        const int maximumBatchBytes = 256 * 1024;
        var drainStarted = Stopwatch.GetTimestamp();
        var chunks = new List<SerialBytesReceivedEventArgs>();
        var totalLength = 0;
        while (_receiveQueue.TryDequeue(out var chunk))
        {
            chunks.Add(chunk);
            totalLength += chunk.Data.Length;
            if (totalLength >= maximumBatchBytes ||
                Stopwatch.GetElapsedTime(drainStarted) >= TimeSpan.FromMilliseconds(8))
            {
                break;
            }
        }

        if (totalLength > 0)
        {
            var data = new byte[totalLength];
            var offset = 0;
            foreach (var chunk in chunks)
            {
                Buffer.BlockCopy(chunk.Data, 0, data, offset, chunk.Data.Length);
                offset += chunk.Data.Length;
            }

            _totalReceivedBytes += data.Length;
            var text = _receiveTextDecoder.Decode(data, GetSelectedTextEncoding());
            AppendTerminalEntry("RX", text, rawBytes: data, timestamp: chunks[0].ReceivedAt);
        }

        if (!_receiveQueue.IsEmpty &&
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, DrainReceiveQueue))
        {
            return;
        }

        // Close the enqueue race: data may arrive after the empty check but before
        // the scheduled flag is released.
        Interlocked.Exchange(ref _receiveDrainScheduled, 0);
        if (!_receiveQueue.IsEmpty)
        {
            ScheduleReceiveQueueDrain();
        }
    }

    private void SerialPort_ErrorOccurred(string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isUnloaded)
            {
                ShowMessage("串口读取异常", message, InfoBarSeverity.Error);
            }
        });
    }

    private void RepeatSendToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_repeatSendTimer is null)
        {
            return;
        }

        if (!RepeatSendToggle.IsOn)
        {
            StopRepeatSending();
            return;
        }

        if (!_serialPortService.IsOpen)
        {
            RepeatSendToggle.IsOn = false;
            ShowMessage("无法循环发送", "请先连接串口。", InfoBarSeverity.Warning);
            return;
        }

        if (!TryPreparePayload(
                SendTextBox.Text,
                SendHexCheckBox.IsChecked == true,
                LineEndingComboBox.SelectedItem as string,
                shouldShowErrors: true,
                out var payload))
        {
            RepeatSendToggle.IsOn = false;
            return;
        }

        Volatile.Write(ref _repeatSendPayload, payload);
        Volatile.Write(ref _repeatSendEnabled, 1);
        Volatile.Write(ref _repeatSendFailureQueued, 0);
        UpdateRepeatSendInterval();
    }

    private void RepeatIntervalNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        UpdateRepeatSendInterval();

    private void UpdateRepeatSendInterval()
    {
        if (_repeatSendTimer is null)
        {
            return;
        }

        var interval = GetRepeatSendInterval();
        if (Volatile.Read(ref _repeatSendEnabled) != 0)
        {
            _repeatSendTimer.Change(interval, interval);
        }
    }

    private TimeSpan GetRepeatSendInterval()
    {
        var value = double.IsNaN(RepeatIntervalNumberBox.Value) ? 1000 : RepeatIntervalNumberBox.Value;
        return TimeSpan.FromMilliseconds(Math.Clamp(value, 20, 60_000));
    }

    private void UpdateRepeatSendPayload()
    {
        if (Volatile.Read(ref _repeatSendEnabled) == 0)
        {
            return;
        }

        Volatile.Write(
            ref _repeatSendPayload,
            TryPreparePayload(
                SendTextBox.Text,
                SendHexCheckBox.IsChecked == true,
                LineEndingComboBox.SelectedItem as string,
                shouldShowErrors: false,
                out var payload)
                ? payload
                : null);
    }

    private void RepeatTimer_Tick()
    {
        if (Volatile.Read(ref _repeatSendEnabled) == 0 ||
            Interlocked.Exchange(ref _repeatSendInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            var payload = Volatile.Read(ref _repeatSendPayload);
            if (payload is null || !_serialPortService.IsOpen)
            {
                QueueRepeatSendFailure();
                return;
            }

            var sentAt = DateTime.Now;
            _serialPortService.Send(payload.Bytes);
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (_isUnloaded)
                {
                    return;
                }

                _totalSentBytes += payload.Bytes.Length;
                AppendTerminalEntry("TX", payload.DisplayText, payload.IsHex, timestamp: sentAt);
                UpdateTransferCounters();
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            QueueRepeatSendFailure();
        }
        finally
        {
            Volatile.Write(ref _repeatSendInProgress, 0);
        }
    }

    private void StopRepeatSending()
    {
        Volatile.Write(ref _repeatSendEnabled, 0);
        Volatile.Write(ref _repeatSendPayload, null);
        _repeatSendTimer?.Stop();
    }

    private void QueueRepeatSendFailure()
    {
        StopRepeatSending();
        if (Interlocked.Exchange(ref _repeatSendFailureQueued, 1) != 0)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _repeatSendFailureQueued, 0);
            if (_isUnloaded)
            {
                return;
            }

            RepeatSendToggle.IsOn = false;
            ShowMessage("循环发送已停止", "请检查连接状态或发送内容。", InfoBarSeverity.Warning);
        });
    }

    private void UpdateConnectionState()
    {
        var isOpen = _serialPortService.IsOpen;
        SettingsPanel.IsHitTestVisible = !isOpen;
        SettingsPanel.Opacity = isOpen ? 0.55 : 1;
        SendButton.IsEnabled = isOpen;
        TerminalView.IsInputEnabled = isOpen;
        ToolTipService.SetToolTip(
            TerminalView,
            isOpen ? "键入内容将同步发送到串口；内容仅显示设备 RX 回传。" : "连接串口后可在内容区键入发送；当前仍可选择和复制内容。");
        FooterConnectionText.Text = isOpen ? $"{_serialPortService.PortName} · 通信中" : "未连接";
        if (App.CurrentWindow is MainWindow window)
        {
            window.SetConnectionStatus(isOpen ? _serialPortService.PortName : null);
        }

        ConnectionDot.Fill = isOpen ? _connectedBrush : _disconnectedBrush;
        OpenCloseText.Text = isOpen ? "断开串口" : "连接串口";
        OpenCloseIcon.Glyph = isOpen ? "\uE8D7" : "\uE8CE";
        if (isOpen)
        {
            PortHintText.Text = "参数已锁定，断开后可修改。";
        }
    }

    private void UpdateTransferCounters()
    {
        ReceiveCountText.Text = $"RX  {FormatByteCount(_totalReceivedBytes)}";
        SendCountText.Text = $"TX  {FormatByteCount(_totalSentBytes)}";
    }
}
