using System.ComponentModel;
using Comet.Models;
using Comet.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Comet.Views;

public sealed partial class MainPage
{
    private void CommandPresets_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(CommandPresetsViewModel.CountText) or
            nameof(CommandPresetsViewModel.IsEmpty))
        {
            UpdatePresetPanelState();
        }
    }

    private void InitializeCommandPresets()
    {
        try
        {
            ViewModel.CommandPresets.Initialize();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Startup behavior remains non-blocking when preferences cannot be saved.
        }

        UpdatePresetPanelState();
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

        // Quick send intentionally bypasses the bottom composer. A preset should not
        // overwrite text that the user is currently preparing there.
        SendPayload(preset.Command, preset.IsHex, preset.LineEnding, shouldShowErrors: true);
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = FindCommandPreset(sender);
        if (preset is null)
        {
            return;
        }

        RunPresetMutation(() => ViewModel.CommandPresets.Delete(preset));
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

        var lineEnding = (NewPresetLineEndingComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "无";

        RunPresetMutation(() =>
            ViewModel.CommandPresets.Add(
                NewPresetNameTextBox.Text,
                command,
                NewPresetHexCheckBox.IsChecked == true,
                lineEnding));
        if (ViewModel.CommandPresets.Items.Count > 0)
        {
            PresetList.ScrollIntoView(ViewModel.CommandPresets.Items[^1]);
        }

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

        // The item template uses one-way bindings. Commit edits explicitly on focus
        // loss, then normalize the visible value before persisting the collection.
        RunPresetMutation(() => ViewModel.CommandPresets.UpdateName(preset, textBox.Text));
        textBox.Text = preset.Name;
    }

    private void PresetCommandTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: CommandPresetModel preset } textBox)
        {
            return;
        }

        RunPresetMutation(() => ViewModel.CommandPresets.UpdateCommand(preset, textBox.Text));
    }

    private CommandPresetModel? FindCommandPreset(object sender)
    {
        if (sender is not FrameworkElement { Tag: string id })
        {
            return null;
        }

        // Resolve by the stable model identifier because ListView containers may be
        // recycled and the clicked element is not itself the data item.
        return ViewModel.CommandPresets.Find(id);
    }

    private void LoadPresetIntoSendComposer(CommandPresetModel preset)
    {
        SendTextBox.Text = preset.Command;
        SendHexCheckBox.IsChecked = preset.IsHex;
        LineEndingComboBox.SelectedItem = preset.LineEnding;
    }

    private void UpdatePresetPanelState()
    {
        PresetCountText.Text = ViewModel.CommandPresets.CountText;
        EmptyPresetText.Visibility = ViewModel.CommandPresets.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RunPresetMutation(Action mutation)
    {
        try
        {
            mutation();
            UpdatePresetPanelState();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowMessage("预设保存失败", exception.Message, InfoBarSeverity.Error);
            UpdatePresetPanelState();
        }
    }
}
