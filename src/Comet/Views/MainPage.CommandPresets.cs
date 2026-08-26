using System.ComponentModel;
using Comet.Models;
using Comet.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Comet.Views;

public sealed partial class MainPage
{
    // A drag may start only after the dedicated handle arms the next ListView drag.
    private bool _isPresetDragHandleArmed;

    // Failed saves leave the reordered collection active for the current session.
    private bool _isPresetOrderDirty;
    private bool _isPresetReorderMode;

    private void CommandPresets_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CommandPresetsViewModel.CountText))
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
            // Startup remains non-blocking when preferences cannot be read.
        }

        UpdatePresetPanelState();
    }

    private void TogglePresetPanelButton_Click(object sender, RoutedEventArgs e)
    {
        var shouldShowPanel = PresetPanel.Visibility != Visibility.Visible;
        if (!shouldShowPanel)
        {
            ExitPresetReorderMode();
        }

        PresetPanel.Visibility = shouldShowPanel ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(PresetPanelToggleButton, shouldShowPanel ? "隐藏快捷指令" : "显示快捷指令");
    }

    private void PresetReorderModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPresetReorderMode)
        {
            CompletePresetReorderMode();
        }
        else
        {
            SetPresetReorderMode(true);
        }
    }

    private void CompletePresetReorderMode()
    {
        if (_isPresetOrderDirty)
        {
            RunPresetMutation(ViewModel.CommandPresets.SaveOrder);
        }

        SetPresetReorderMode(false);
    }

    private void ExitPresetReorderMode()
    {
        if (!_isPresetReorderMode)
        {
            return;
        }

        SetPresetReorderMode(false);
    }

    private void SetPresetReorderMode(bool isEnabled)
    {
        _isPresetReorderMode = isEnabled;
        _isPresetDragHandleArmed = false;
        PresetList.AllowDrop = isEnabled;
        PresetList.CanDragItems = isEnabled;
        PresetList.CanReorderItems = isEnabled;
        PresetReorderModeButton.Content = isEnabled ? "完成" : "编辑排序";
        ToolTipService.SetToolTip(
            PresetReorderModeButton,
            isEnabled ? "完成顺序调整" : "启用拖拽排序");
    }

    private void PresetDragHandle_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        _isPresetDragHandleArmed = _isPresetReorderMode;
    }

    private void PresetDragHandle_PointerEnded(object sender, PointerRoutedEventArgs args)
    {
        _isPresetDragHandleArmed = false;
    }

    private void PresetList_DragItemsStarting(object sender, DragItemsStartingEventArgs args)
    {
        args.Cancel = !_isPresetReorderMode || !_isPresetDragHandleArmed;
        _isPresetDragHandleArmed = false;
    }

    private void PresetList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _isPresetDragHandleArmed = false;
        // ListView has already updated the observable collection. Defer the single
        // persistence attempt until the user explicitly completes reorder mode.
        _isPresetOrderDirty = true;
    }

    private void LoadPresetButton_Click(object sender, RoutedEventArgs e)
    {
        ExitPresetReorderMode();
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
        ExitPresetReorderMode();
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
        ExitPresetReorderMode();
        var preset = FindCommandPreset(sender);
        if (preset is null)
        {
            return;
        }

        RunPresetMutation(() => ViewModel.CommandPresets.Delete(preset));
    }

    private void AddPresetButton_Click(object sender, RoutedEventArgs e)
    {
        ExitPresetReorderMode();
        if (!ViewModel.CommandPresets.CanAdd)
        {
            ShowMessage(
                "无法添加指令",
                "快捷指令已达到数量上限。",
                InfoBarSeverity.Warning);
            return;
        }

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

        if (_isPresetReorderMode)
        {
            textBox.Text = preset.Name;
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

        if (_isPresetReorderMode)
        {
            textBox.Text = preset.Command;
            return;
        }

        RunPresetMutation(() => ViewModel.CommandPresets.UpdateCommand(preset, textBox.Text));
    }

    internal void ExitCommandPresetReorderMode() => ExitPresetReorderMode();

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
        PresetReorderModeButton.IsEnabled = _isPresetReorderMode || ViewModel.CommandPresets.Items.Count > 1;
    }

    private void RunPresetMutation(Action mutation)
    {
        try
        {
            mutation();
            // Every successful mutation writes the complete collection, including
            // the current item order, so no pending reorder remains afterward.
            _isPresetOrderDirty = false;
            UpdatePresetPanelState();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowMessage("预设保存失败", exception.Message, InfoBarSeverity.Error);
            UpdatePresetPanelState();
        }
    }
}
