using Comet.Models;
using Comet.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Comet;

public sealed partial class MainPage
{
    private void InitializeCommandPresets()
    {
        PresetList.ItemsSource = _commandPresets;
        foreach (var preset in CommandPresetStore.Load())
        {
            _commandPresets.Add(preset);
        }

        UpdatePresetUi();
        PersistCommandPresets(showError: false);
    }

    private void TogglePresetPanelButton_Click(object sender, RoutedEventArgs e)
    {
        var showPanel = PresetPanel.Visibility != Visibility.Visible;
        PresetPanel.Visibility = showPanel ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(PresetPanelToggleButton, showPanel ? "隐藏快捷指令" : "显示快捷指令");
    }

    private void LoadPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = FindPreset(sender);
        if (preset is null)
        {
            return;
        }

        ApplyPreset(preset);
        SendTextBox.Focus(FocusState.Programmatic);
    }

    private void SendPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = FindPreset(sender);
        if (preset is null)
        {
            return;
        }

        SendPayload(preset.Command, preset.IsHex, preset.LineEnding, showErrors: true);
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = FindPreset(sender);
        if (preset is null)
        {
            return;
        }

        _commandPresets.Remove(preset);
        UpdatePresetUi();
        PersistCommandPresets(showError: true);
    }

    private void AddPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var command = NewPresetCommandTextBox.Text;
        if (string.IsNullOrWhiteSpace(command))
        {
            ShowMessage("无法添加指令", "请在快捷指令面板中输入指令内容。", InfoBarSeverity.Warning);
            NewPresetCommandTextBox.Focus(FocusState.Programmatic);
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewPresetNameTextBox.Text)
            ? $"指令 {_commandPresets.Count + 1}"
            : NewPresetNameTextBox.Text.Trim();
        var lineEnding = (NewPresetLineEndingComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "无";

        _commandPresets.Add(new CommandPreset
        {
            Name = name,
            Command = command,
            IsHex = NewPresetHexCheckBox.IsChecked == true,
            LineEnding = lineEnding
        });

        UpdatePresetUi();
        PersistCommandPresets(showError: true);
        PresetList.ScrollIntoView(_commandPresets[^1]);
        NewPresetNameTextBox.Text = string.Empty;
        NewPresetCommandTextBox.Text = string.Empty;
        NewPresetCommandTextBox.Focus(FocusState.Programmatic);
    }

    private void PresetNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: CommandPreset preset } textBox)
        {
            return;
        }

        preset.Name = string.IsNullOrWhiteSpace(textBox.Text) ? "未命名指令" : textBox.Text.Trim();
        textBox.Text = preset.Name;
        PersistCommandPresets(showError: true);
    }

    private void PresetCommandTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: CommandPreset preset } textBox)
        {
            return;
        }

        preset.Command = textBox.Text;
        PersistCommandPresets(showError: true);
    }

    private CommandPreset? FindPreset(object sender)
    {
        if (sender is not FrameworkElement { Tag: string id })
        {
            return null;
        }

        return _commandPresets.FirstOrDefault(preset => preset.Id == id);
    }

    private void ApplyPreset(CommandPreset preset)
    {
        SendTextBox.Text = preset.Command;
        SendHexCheckBox.IsChecked = preset.IsHex;
        LineEndingComboBox.SelectedItem = preset.LineEnding;
    }

    private void UpdatePresetUi()
    {
        PresetCountText.Text = $"{_commandPresets.Count} 条预设";
        EmptyPresetText.Visibility = _commandPresets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PersistCommandPresets(bool showError)
    {
        try
        {
            CommandPresetStore.Save(_commandPresets);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (showError)
            {
                ShowMessage("预设保存失败", exception.Message, InfoBarSeverity.Error);
            }
        }
    }
}
