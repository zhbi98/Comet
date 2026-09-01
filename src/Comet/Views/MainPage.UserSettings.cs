using Comet.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Comet.Views;

public sealed partial class MainPage
{
    private bool _isApplyingUserSettings;
    private bool _shouldApplyStoredPortSelection = true;

    /// <summary>
    /// Applies persisted preferences after option lists have been populated.
    /// </summary>
    private void ApplyUserSettings()
    {
        RunWithoutUserSettingsSave(() =>
        {
            var settings = ViewModel.UserSettings.Current;

            ReceiveHexCheckBox.IsChecked = settings.Terminal.ReceiveAsHex;
            TimestampCheckBox.IsChecked = settings.Terminal.TimestampEnabled;
            AutoScrollCheckBox.IsChecked = settings.Terminal.AutoScrollEnabled;
            ViewModel.Terminal.SetReceiveAsHex(settings.Terminal.ReceiveAsHex);
            TerminalView.AutoScroll = settings.Terminal.AutoScrollEnabled;

            SelectComboBoxItem(BaudRateComboBox, settings.Serial.BaudRate);
            SelectComboBoxItem(DataBitsComboBox, settings.Serial.DataBits);
            SelectComboBoxItem(StopBitsComboBox, settings.Serial.StopBits);
            SelectComboBoxItem(ParityComboBox, settings.Serial.Parity);
            SelectComboBoxItem(HandshakeComboBox, settings.Serial.Handshake);
            SelectComboBoxItem(EncodingComboBox, settings.Serial.EncodingName);

            SendHexCheckBox.IsChecked = settings.Send.IsHex;
            SelectComboBoxItem(LineEndingComboBox, settings.Send.LineEnding);
            RepeatIntervalNumberBox.Value = Math.Clamp(settings.Send.RepeatIntervalMs, 20, 60_000);
        });
    }

    private void ApplyStoredPortSelection()
    {
        if (!_shouldApplyStoredPortSelection)
        {
            return;
        }

        _shouldApplyStoredPortSelection = false;
        var lastPortName = ViewModel.UserSettings.Current.Serial.LastPortName;
        if (string.IsNullOrWhiteSpace(lastPortName))
        {
            return;
        }

        var selectedPort = ViewModel.Connection.Ports.FirstOrDefault(port =>
            string.Equals(port.PortName, lastPortName, StringComparison.OrdinalIgnoreCase));
        if (selectedPort is null)
        {
            return;
        }

        PortComboBox.SelectedItem = selectedPort;
        ViewModel.Connection.SelectedPort = selectedPort;
    }

    private void AttachUserSettingsChangeHandlers()
    {
        AttachTerminalSettingsChangeHandlers();
        AttachSerialSettingsChangeHandlers();
        AttachSendSettingsChangeHandlers();
    }

    private void AttachTerminalSettingsChangeHandlers()
    {
        TimestampCheckBox.Click += (_, _) => SaveUserSettings();
        AutoScrollCheckBox.Click += (_, _) =>
        {
            TerminalView.AutoScroll = AutoScrollCheckBox.IsChecked == true;
            if (TerminalView.AutoScroll)
            {
                TerminalView.ScrollToEnd();
            }

            SaveUserSettings();
        };
    }

    private void AttachSerialSettingsChangeHandlers()
    {
        PortComboBox.SelectionChanged += (_, _) => SaveUserSettings();
        BaudRateComboBox.SelectionChanged += (_, _) => SaveUserSettings();
        DataBitsComboBox.SelectionChanged += (_, _) => SaveUserSettings();
        StopBitsComboBox.SelectionChanged += (_, _) => SaveUserSettings();
        ParityComboBox.SelectionChanged += (_, _) => SaveUserSettings();
        HandshakeComboBox.SelectionChanged += (_, _) => SaveUserSettings();
        EncodingComboBox.SelectionChanged += (_, _) => SaveUserSettings();
    }

    private void AttachSendSettingsChangeHandlers()
    {
        SendHexCheckBox.Click += (_, _) =>
        {
            UpdateRepeatSendPayload();
            SaveUserSettings();
        };
        LineEndingComboBox.SelectionChanged += (_, _) =>
        {
            UpdateRepeatSendPayload();
            SaveUserSettings();
        };
        RepeatIntervalNumberBox.ValueChanged += (_, _) => SaveUserSettings();
    }

    private void SaveUserSettings()
    {
        if (_isApplyingUserSettings)
        {
            return;
        }

        try
        {
            ViewModel.UserSettings.Save(CaptureUserSettings());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Settings are preferences only. Keep the current in-memory state usable
            // when the configuration file cannot be written.
        }
    }

    private void RunWithoutUserSettingsSave(Action action)
    {
        _isApplyingUserSettings = true;
        try
        {
            action();
        }
        finally
        {
            _isApplyingUserSettings = false;
        }
    }

    private AppSettingsModel CaptureUserSettings() => new()
    {
        Terminal = new TerminalDisplaySettingsModel
        {
            FontFamilyName = ViewModel.TerminalAppearance.FontFamilyName,
            FontSize = ViewModel.TerminalAppearance.FontSize,
            ReceiveAsHex = ReceiveHexCheckBox.IsChecked == true,
            TimestampEnabled = TimestampCheckBox.IsChecked == true,
            AutoScrollEnabled = AutoScrollCheckBox.IsChecked == true
        },
        Serial = new SerialSettingsModel
        {
            LastPortName = (PortComboBox.SelectedItem as SerialPortInfoModel)?.PortName,
            BaudRate = (int)(BaudRateComboBox.SelectedItem ?? SerialSettingsModel.DEFAULT_BAUD_RATE),
            EncodingName = EncodingComboBox.SelectedItem as string ?? SerialSettingsModel.DEFAULT_ENCODING_NAME,
            DataBits = (int)(DataBitsComboBox.SelectedItem ?? SerialSettingsModel.DEFAULT_DATA_BITS),
            StopBits = StopBitsComboBox.SelectedItem as string ?? SerialSettingsModel.DEFAULT_STOP_BITS,
            Parity = ParityComboBox.SelectedItem as string ?? SerialSettingsModel.DEFAULT_PARITY,
            Handshake = HandshakeComboBox.SelectedItem as string ?? SerialSettingsModel.DEFAULT_HANDSHAKE
        },
        Send = new SendSettingsModel
        {
            IsHex = SendHexCheckBox.IsChecked == true,
            LineEnding = LineEndingComboBox.SelectedItem as string ?? SendSettingsModel.DEFAULT_LINE_ENDING,
            RepeatIntervalMs = double.IsNaN(RepeatIntervalNumberBox.Value)
                ? SendSettingsModel.DEFAULT_REPEAT_INTERVAL_MS
                : Math.Clamp(RepeatIntervalNumberBox.Value, 20, 60_000)
        }
    };

    private static void SelectComboBoxItem(ComboBox comboBox, object value)
    {
        if (comboBox.Items.Contains(value))
        {
            comboBox.SelectedItem = value;
        }
    }
}
