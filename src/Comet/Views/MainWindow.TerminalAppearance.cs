using Comet.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Comet.Views;

public sealed partial class MainWindow
{
    private static readonly string[] _supportedTerminalFontFamilies =
    [
        "Cascadia Mono",
        "Cascadia Code",
        "Consolas",
        "Courier New",
        "Lucida Console"
    ];

    private void InitializeTerminalAppearance()
    {
        TerminalFontFamilyComboBox.ItemsSource = _supportedTerminalFontFamilies;
        TerminalFontSizeNumberBox.Minimum = TerminalAppearanceViewModel.MIN_FONT_SIZE;
        TerminalFontSizeNumberBox.Maximum = TerminalAppearanceViewModel.MAX_FONT_SIZE;
        SynchronizeTerminalFontControls();
    }

    private async void TerminalFontSettingsMenuItem_Click(object sender, RoutedEventArgs args)
    {
        // The menu only opens the editor. Typography state remains in the view model
        // and MainPage independently projects that state onto the terminal control.
        SynchronizeTerminalFontControls();
        await TerminalFontSettingsDialog.ShowAsync();
    }

    private void TerminalFontFamilyComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (TerminalFontFamilyComboBox.SelectedItem is not string fontFamilyName)
        {
            return;
        }

        _viewModel.TerminalAppearance.FontFamilyName = fontFamilyName;
        UpdateTerminalFontPreview();
    }

    private void TerminalFontSizeNumberBox_ValueChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue))
        {
            return;
        }

        _viewModel.TerminalAppearance.FontSize = args.NewValue;
        UpdateTerminalFontPreview();
    }

    private void ResetTerminalFontButton_Click(object sender, RoutedEventArgs args)
    {
        _viewModel.TerminalAppearance.Reset();
        SynchronizeTerminalFontControls();
    }

    private void SynchronizeTerminalFontControls()
    {
        var appearance = _viewModel.TerminalAppearance;
        TerminalFontFamilyComboBox.SelectedItem = appearance.FontFamilyName;
        TerminalFontSizeNumberBox.Value = appearance.FontSize;
        UpdateTerminalFontPreview();
    }

    private void UpdateTerminalFontPreview()
    {
        var appearance = _viewModel.TerminalAppearance;
        TerminalFontPreviewTextBlock.FontFamily = new FontFamily(appearance.FontFamilyName);
        TerminalFontPreviewTextBlock.FontSize = appearance.FontSize;
    }
}
