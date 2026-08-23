using Comet.Services;
using Comet.Utilities;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Comet;

public sealed partial class MainPage
{
    private void RefreshPortsButton_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void RefreshPorts()
    {
        var previous = PortComboBox.SelectedItem as string;
        var ports = SerialPortService.GetAvailablePorts();
        PortComboBox.ItemsSource = ports;

        if (previous is not null && ports.Contains(previous))
        {
            PortComboBox.SelectedItem = previous;
        }
        else if (ports.Count > 0)
        {
            PortComboBox.SelectedIndex = 0;
        }

        PortHintText.Text = ports.Count == 0
            ? "未发现串口，请检查设备驱动或 USB 连接。"
            : $"发现 {ports.Count} 个串口：{string.Join("、", ports)}";
    }

    private void OpenCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_serialPort.IsOpen)
        {
            Disconnect();
            return;
        }

        if (PortComboBox.SelectedItem is not string portName)
        {
            ShowMessage("没有可用串口", "请连接设备并刷新串口列表。", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var settings = new SerialPortSettings(
                portName,
                (int)(BaudRateComboBox.SelectedItem ?? 115200),
                (int)(DataBitsComboBox.SelectedItem ?? 8),
                SerialPortOptions.ParseParity(ParityComboBox.SelectedItem as string),
                SerialPortOptions.ParseStopBits(StopBitsComboBox.SelectedItem as string),
                SerialPortOptions.ParseHandshake(HandshakeComboBox.SelectedItem as string),
                DtrToggle.IsOn,
                RtsToggle.IsOn);

            _serialPort.Open(settings);
            var parity = SerialPortOptions.GetParityShortName(settings.Parity);
            var stopBits = SerialPortOptions.GetStopBitsShortName(settings.StopBits);
            AppendEntry("SYS", $"已连接 {portName}  ·  {settings.BaudRate} / {settings.DataBits}{parity}{stopBits}");
            UpdateConnectionUi();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or ArgumentException or InvalidOperationException)
        {
            ShowMessage("连接失败", exception.Message, InfoBarSeverity.Error);
            UpdateConnectionUi();
        }
    }

    private void Disconnect()
    {
        var portName = _serialPort.PortName ?? "串口";
        _repeatTimer.Stop();
        RepeatSendToggle.IsOn = false;
        _serialPort.Close();
        AppendEntry("SYS", $"{portName} 已断开");
        UpdateConnectionUi();
        RefreshPorts();
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => SendCurrentPayload(showErrors: true);

    private bool SendCurrentPayload(bool showErrors) => SendPayload(
        SendTextBox.Text,
        SendHexCheckBox.IsChecked == true,
        LineEndingComboBox.SelectedItem as string,
        showErrors);

    private bool SendPayload(string content, bool isHex, string? lineEnding, bool showErrors)
    {
        if (!_serialPort.IsOpen)
        {
            if (showErrors)
            {
                ShowMessage("无法发送", "请先连接串口。", InfoBarSeverity.Warning);
            }

            return false;
        }

        byte[] payload;
        string displayText;
        if (isHex)
        {
            if (!HexCodec.TryParse(content, out payload, out var error))
            {
                if (showErrors)
                {
                    ShowMessage("HEX 格式错误", error, InfoBarSeverity.Warning);
                }

                return false;
            }

            displayText = HexCodec.Format(payload);
        }
        else
        {
            if (!TextEscapeCodec.TryDecode(content, out var decodedText, out var escapeError))
            {
                if (showErrors)
                {
                    ShowMessage("转义格式错误", escapeError, InfoBarSeverity.Warning);
                }

                return false;
            }

            var text = decodedText + GetLineEnding(lineEnding);
            if (text.Length == 0)
            {
                if (showErrors)
                {
                    ShowMessage("没有发送内容", "请输入文本或选择一个行尾符。", InfoBarSeverity.Warning);
                }

                return false;
            }

            payload = GetSelectedEncoding().GetBytes(text);
            displayText = text;
        }

        try
        {
            _serialPort.Send(payload);
            _sentBytes += payload.Length;
            AppendEntry("TX", displayText, isHex);
            UpdateCounters();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            if (showErrors)
            {
                ShowMessage("发送失败", exception.Message, InfoBarSeverity.Error);
            }

            return false;
        }
    }

    private void SerialPort_BytesReceived(object? sender, SerialBytesReceivedEventArgs e)
    {
        _receiveQueue.Enqueue(e.Data);
        if (Interlocked.Exchange(ref _receiveDispatchScheduled, 1) != 0)
        {
            return;
        }

        if (!DispatcherQueue.TryEnqueue(DrainReceivedData))
        {
            Interlocked.Exchange(ref _receiveDispatchScheduled, 0);
        }
    }

    private void DrainReceivedData()
    {
        Interlocked.Exchange(ref _receiveDispatchScheduled, 0);
        if (_isUnloaded)
        {
            while (_receiveQueue.TryDequeue(out _))
            {
            }

            return;
        }

        var chunks = new List<byte[]>();
        var totalLength = 0;
        while (_receiveQueue.TryDequeue(out var chunk))
        {
            chunks.Add(chunk);
            totalLength += chunk.Length;
        }

        if (totalLength == 0)
        {
            return;
        }

        var data = new byte[totalLength];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, data, offset, chunk.Length);
            offset += chunk.Length;
        }

        _receivedBytes += data.Length;
        var text = GetSelectedEncoding().GetString(data);
        AppendEntry("RX", text, rawBytes: data);
        UpdateCounters();
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
        if (_repeatTimer is null)
        {
            return;
        }

        if (!RepeatSendToggle.IsOn)
        {
            _repeatTimer.Stop();
            return;
        }

        if (!_serialPort.IsOpen)
        {
            RepeatSendToggle.IsOn = false;
            ShowMessage("无法循环发送", "请先连接串口。", InfoBarSeverity.Warning);
            return;
        }

        UpdateRepeatInterval();
        _repeatTimer.Start();
    }

    private void RepeatIntervalNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        UpdateRepeatInterval();

    private void UpdateRepeatInterval()
    {
        if (_repeatTimer is null)
        {
            return;
        }

        var value = double.IsNaN(RepeatIntervalNumberBox.Value) ? 1000 : RepeatIntervalNumberBox.Value;
        _repeatTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(value, 20, 60_000));
    }

    private void RepeatTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (SendCurrentPayload(showErrors: false))
        {
            return;
        }

        RepeatSendToggle.IsOn = false;
        ShowMessage("循环发送已停止", "请检查连接状态或发送内容。", InfoBarSeverity.Warning);
    }

    private void UpdateConnectionUi()
    {
        var isOpen = _serialPort.IsOpen;
        SettingsPanel.IsHitTestVisible = !isOpen;
        SettingsPanel.Opacity = isOpen ? 0.55 : 1;
        SendButton.IsEnabled = isOpen;
        TerminalTextBox.IsReadOnly = !isOpen;
        ToolTipService.SetToolTip(
            TerminalTextBox,
            isOpen ? "键入内容将同步发送到串口；内容仅显示设备 RX 回传。" : "连接串口后可在内容区键入发送；当前仍可选择和复制内容。");
        FooterConnectionText.Text = isOpen ? $"{_serialPort.PortName} · 通信中" : "未连接";
        if (App.CurrentWindow is MainWindow window)
        {
            window.SetConnectionStatus(isOpen ? _serialPort.PortName : null);
        }

        ConnectionDot.Fill = isOpen ? _connectedBrush : _disconnectedBrush;
        OpenCloseText.Text = isOpen ? "断开串口" : "连接串口";
        OpenCloseIcon.Glyph = isOpen ? "\uE8D7" : "\uE8CE";
        if (isOpen)
        {
            PortHintText.Text = "参数已锁定，断开后可修改。";
        }
    }

    private void UpdateCounters()
    {
        ReceiveCountText.Text = $"RX  {FormatByteCount(_receivedBytes)}";
        SendCountText.Text = $"TX  {FormatByteCount(_sentBytes)}";
    }
}
