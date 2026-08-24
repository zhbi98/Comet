using Comet.Models;
using Comet.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Comet.Views;

public sealed partial class MainPage
{
    private void InitializeCommandPresets()
    {
        PresetList.ItemsSource = _commandPresets;
        foreach (var preset in CommandPresetStorageService.LoadPresets())
        {
            _commandPresets.Add(preset);
        }

        UpdatePresetPanelState();
        SaveCommandPresets(shouldShowError: false);
    }

    private void TogglePresetPanelButton_Click(object sender, RoutedEventArgs e)
    {
        var shouldShowPanel = PresetPanel.Visibility != Visibility.Visible;
        PresetPanel.Visibility = shouldShowPanel ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(PresetPanelToggleButton, shouldShowPanel ? "隐藏快捷指令" : "显示快捷指令");
    }

    private void LoadPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = FindCommandPreset(sender);
        if (preset is null)
        {
            return;
        }

        LoadPresetIntoSendComposer(preset);
        SendTextBox.Focus(FocusState.Programmatic);
    }

    private void SendPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = FindCommandPreset(sender);
        if (preset is null)
        {
            return;
        }

        SendPayload(preset.Command, preset.IsHex, preset.LineEnding, shouldShowErrors: true);
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = FindCommandPreset(sender);
        if (preset is null)
        {
            return;
        }

        _commandPresets.Remove(preset);
        UpdatePresetPanelState();
        SaveCommandPresets(shouldShowError: true);
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

        _commandPresets.Add(new CommandPresetModel
        {
            Name = name,
            Command = command,
            IsHex = NewPresetHexCheckBox.IsChecked == true,
            LineEnding = lineEnding
        });

        UpdatePresetPanelState();
        SaveCommandPresets(shouldShowError: true);
        PresetList.ScrollIntoView(_commandPresets[^1]);
        NewPresetNameTextBox.Text = string.Empty;
        NewPresetCommandTextBox.Text = string.Empty;
        NewPresetCommandTextBox.Focus(FocusState.Programmatic);
    }

    private void PresetNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: CommandPresetModel preset } textBox)
        {
            return;
        }

        preset.Name = string.IsNullOrWhiteSpace(textBox.Text) ? "未命名指令" : textBox.Text.Trim();
        textBox.Text = preset.Name;
        SaveCommandPresets(shouldShowError: true);
    }

    private void PresetCommandTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: CommandPresetModel preset } textBox)
        {
            return;
        }

        preset.Command = textBox.Text;
        SaveCommandPresets(shouldShowError: true);
    }

    private CommandPresetModel? FindCommandPreset(object sender)
    {
        if (sender is not FrameworkElement { Tag: string id })
        {
            return null;
        }

        return _commandPresets.FirstOrDefault(preset => preset.Id == id);
    }

    private void LoadPresetIntoSendComposer(CommandPresetModel preset)
    {
        SendTextBox.Text = preset.Command;
        SendHexCheckBox.IsChecked = preset.IsHex;
        LineEndingComboBox.SelectedItem = preset.LineEnding;
    }

    private void UpdatePresetPanelState()
    {
        PresetCountText.Text = $"{_commandPresets.Count} 条预设";
        EmptyPresetText.Visibility = _commandPresets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveCommandPresets(bool shouldShowError)
    {
        try
        {
            CommandPresetStorageService.SavePresets(_commandPresets);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (shouldShowError)
            {
                ShowMessage("预设保存失败", exception.Message, InfoBarSeverity.Error);
            }
        }
    }
}
