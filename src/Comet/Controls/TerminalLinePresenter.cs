using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Comet.Controls;

/// <summary>
/// Renders one recycled terminal row. Document-level interaction state remains in
/// <see cref="VirtualTerminalControl"/> so recycling cannot lose selection or caret positions.
/// </summary>
public sealed class TerminalLinePresenter : Grid
{
    private readonly Rectangle _selection = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Stretch,
        Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(82, 0, 120, 215)),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };

    private readonly TextBlock _text = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
        IsTextSelectionEnabled = false,
        IsHitTestVisible = false
    };

    private readonly Rectangle _caret = new()
    {
        Width = 1.5,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };

    public TerminalLinePresenter()
    {
        IsHitTestVisible = false;
        // Layer order keeps selection behind glyphs and the caret above both.
        Children.Add(_selection);
        Children.Add(_text);
        Children.Add(_caret);
    }

    internal void Update(
        string text,
        double lineHeight,
        double caretHeight,
        double horizontalPadding,
        double characterWidth,
        FontFamily fontFamily,
        double fontSize,
        Brush foreground,
        int selectionStartCell,
        int selectionEndCell,
        bool includesLineBreak,
        int caretCell,
        bool showCaret)
    {
        Height = lineHeight;
        _text.Text = text;
        _text.FontFamily = fontFamily;
        _text.FontSize = fontSize;
        _text.Foreground = foreground;
        _text.Margin = new Thickness(horizontalPadding, 0, horizontalPadding, 0);
        _caret.Fill = foreground;

        // Match the caret to the measured font line box instead of applying fixed
        // insets. Centering that box keeps the caret aligned when the font or size changes.
        var resolvedCaretHeight = Math.Clamp(caretHeight, 1, lineHeight);
        var caretTop = Math.Max(0, (lineHeight - resolvedCaretHeight) / 2);
        _caret.Height = resolvedCaretHeight;
        _caret.Margin = new Thickness(
            horizontalPadding + (Math.Max(0, caretCell) * characterWidth),
            caretTop,
            0,
            0);
        _caret.Visibility = showCaret ? Visibility.Visible : Visibility.Collapsed;

        var selectedCells = Math.Max(0, selectionEndCell - selectionStartCell);
        if (selectedCells == 0 && includesLineBreak)
        {
            // Hard line breaks have no visible glyph but still belong to copied text.
            selectedCells = 1;
        }

        if (selectedCells == 0)
        {
            _selection.Visibility = Visibility.Collapsed;
            return;
        }

        _selection.Margin = new Thickness(
            horizontalPadding + (selectionStartCell * characterWidth),
            1,
            0,
            1);
        _selection.Width = Math.Max(characterWidth, selectedCells * characterWidth);
        _selection.Visibility = Visibility.Visible;
    }
}
