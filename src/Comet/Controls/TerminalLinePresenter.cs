using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Comet.Controls;

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
        VerticalAlignment = VerticalAlignment.Stretch,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };

    public TerminalLinePresenter()
    {
        IsHitTestVisible = false;
        Children.Add(_selection);
        Children.Add(_text);
        Children.Add(_caret);
    }

    internal void Update(
        string text,
        double lineHeight,
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
        _caret.Margin = new Thickness(
            horizontalPadding + (Math.Max(0, caretCell) * characterWidth),
            2,
            0,
            2);
        _caret.Visibility = showCaret ? Visibility.Visible : Visibility.Collapsed;

        var selectedCells = Math.Max(0, selectionEndCell - selectionStartCell);
        if (selectedCells == 0 && includesLineBreak)
        {
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
