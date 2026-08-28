using System.Diagnostics;
using Comet.Core.Transmission;
using Comet.Converters;
using Comet.Models;
using Comet.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Comet.Views;

public sealed partial class MainPage
{
    private void RefreshPortsButton_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void RefreshPorts()
    {
        ViewModel.Connection.SelectedPort = PortComboBox.SelectedItem as SerialPortInfoModel;
        ViewModel.Connection.RefreshPorts();
        PortComboBox.SelectedItem = ViewModel.Connection.SelectedPort;
        PortHintText.Text = ViewModel.Connection.PortHint;
    }

    private async void SerialOpenCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Connection.IsConnected)
        {
            await DisconnectSerialPortAsync();
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

            ViewModel.Connection.Open(connectionOptions);
            ViewModel.Terminal.ResetDecoder();
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

    private async Task DisconnectSerialPortAsync()
    {
        var portName = ViewModel.Connection.PortName ?? "串口";
        StopScheduledSending();
        RepeatSendToggle.IsOn = false;
        PresetCycleToggleButton.IsChecked = false;
        UpdatePresetPanelState();
        await StopReceiveRecordingAsync(showConfirmation: false);
        ViewModel.Connection.Close();
        ViewModel.Terminal.ResetDecoder();
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
        if (!ViewModel.Connection.IsConnected)
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
            ViewModel.Connection.Send(payload.Bytes);
            ViewModel.Terminal.RecordSent(payload.Bytes.Length);
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
        if (ViewModel.Transmission.TryPrepareComposerPayload(
                content,
                isHex,
                lineEnding,
                GetSelectedTextEncoding(),
                out payload,
                out var error))
        {
            return true;
        }

        if (shouldShowErrors)
        {
            var title = error.Kind switch
            {
                SerialPayloadErrorKind.InvalidHex => "HEX 格式错误",
                SerialPayloadErrorKind.InvalidEscape => "转义格式错误",
                _ => "没有发送内容"
            };
            ShowMessage(title, error.Message, InfoBarSeverity.Warning);
        }

        payload = null!;
        return false;
    }

    private void SerialPort_BytesReceived(object? sender, SerialBytesReceivedEventArgs e)
    {
        // Raw recording branches before the UI queue and never reads terminal text.
        ViewModel.ReceiveRecording.TryRecord(e.Data);

        // SerialPort raises this callback on a worker thread. Keep it non-blocking and
        // transfer ownership of UI work to a single dispatcher drain.
        _receiveQueue.Enqueue(e);
        ScheduleReceiveQueueDrain();
    }

    private void ScheduleReceiveQueueDrain()
    {
        // The flag prevents a burst of transport callbacks from flooding the UI queue
        // with one dispatcher operation per serial read.
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

        // Bound both bytes and dispatcher time so sustained input cannot monopolize
        // the UI thread while still amortizing allocations across small serial reads.
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

            ViewModel.Terminal.RecordReceived(data.Length);
            var text = ViewModel.Terminal.DecodeReceived(data, GetSelectedTextEncoding());
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
        if (!RepeatSendToggle.IsOn)
        {
            StopScheduledSending(ScheduledSendMode.RepeatPayload);
            return;
        }

        if (!ViewModel.Connection.IsConnected)
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

        if (PresetCycleToggleButton.IsChecked == true)
        {
            StopCommandPresetCycleSending();
        }

        ViewModel.ScheduledSending.StartRepeating(payload, GetRepeatSendInterval());
    }

    private void RepeatIntervalNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        UpdateRepeatSendInterval();

    private void UpdateRepeatSendInterval()
    {
        ViewModel.ScheduledSending.UpdateInterval(GetRepeatSendInterval());
    }

    private TimeSpan GetRepeatSendInterval()
    {
        var value = double.IsNaN(RepeatIntervalNumberBox.Value) ? 1000 : RepeatIntervalNumberBox.Value;
        return TimeSpan.FromMilliseconds(Math.Clamp(value, 20, 60_000));
    }

    private void UpdateRepeatSendPayload()
    {
        if (ViewModel.ScheduledSending.Mode != ScheduledSendMode.RepeatPayload)
        {
            return;
        }

        ViewModel.ScheduledSending.UpdateRepeatingPayload(
            TryPreparePayload(
                SendTextBox.Text,
                SendHexCheckBox.IsChecked == true,
                LineEndingComboBox.SelectedItem as string,
                shouldShowErrors: false,
                out var payload)
                ? payload
                : null);
    }

    private void ScheduledSending_PayloadSent(object? sender, ScheduledPayloadSentEventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_isUnloaded)
            {
                return;
            }

            ViewModel.Terminal.RecordSent(e.Payload.Bytes.Length);
            AppendTerminalEntry("TX", e.Payload.DisplayText, e.Payload.IsHex, timestamp: e.SentAt);
            UpdateTransferCounters();
        });
    }

    private void StopScheduledSending() => ViewModel.ScheduledSending.Stop();

    private void StopScheduledSending(ScheduledSendMode mode)
        => ViewModel.ScheduledSending.Stop(mode);

    private void ScheduledSending_SendFailed()
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_isUnloaded)
            {
                return;
            }

            var wasPresetCycle = PresetCycleToggleButton.IsChecked == true;
            RepeatSendToggle.IsOn = false;
            PresetCycleToggleButton.IsChecked = false;
            UpdatePresetPanelState();
            ShowMessage(
                wasPresetCycle ? "快捷指令循环发送已停止" : "循环发送已停止",
                "请检查连接状态或发送内容。",
                InfoBarSeverity.Warning);
        });
    }

    private void UpdateConnectionState()
    {
        var isOpen = ViewModel.Connection.IsConnected;
        SerialConnectionSettingsPanel.IsHitTestVisible = !isOpen;
        SerialConnectionSettingsPanel.Opacity = isOpen ? 0.55 : 1;
        SendButton.IsEnabled = isOpen;
        TerminalView.IsInputEnabled = isOpen;
        ToolTipService.SetToolTip(
            TerminalView,
            isOpen ? "键入内容将同步发送到串口；内容仅显示设备 RX 回传。" : "连接串口后可在内容区键入发送；当前仍可选择和复制内容。");
        FooterConnectionText.Text = isOpen ? $"{ViewModel.Connection.PortName} · 通信中" : "未连接";
        if (App.CurrentWindow is MainWindow window)
        {
            window.SetConnectionStatus(isOpen ? ViewModel.Connection.PortName : null);
        }

        ConnectionDot.Fill = isOpen ? _connectedBrush : _disconnectedBrush;
        SerialOpenCloseText.Text = isOpen ? "断开串口" : "连接串口";
        SerialOpenCloseIcon.Glyph = isOpen ? "\uE8D7" : "\uE8CE";
        UpdateReceiveRecordingState();
        if (isOpen)
        {
            PortHintText.Text = "参数已锁定，断开后可修改。";
        }
    }

    private void UpdateTransferCounters()
    {
        ReceiveCountText.Text = ViewModel.Terminal.ReceiveCountText;
        SendCountText.Text = ViewModel.Terminal.SendCountText;
    }
}
