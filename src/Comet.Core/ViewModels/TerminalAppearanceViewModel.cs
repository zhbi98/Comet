namespace Comet.ViewModels;

/// <summary>
/// Holds terminal typography preferences without exposing WinUI font types to the
/// core project. The view converts the family name into its platform font object.
/// </summary>
public sealed class TerminalAppearanceViewModel : ObservableObject
{
    public const string DEFAULT_FONT_FAMILY_NAME = "Cascadia Mono";
    public const double DEFAULT_FONT_SIZE = 13;
    public const double MIN_FONT_SIZE = 10;
    public const double MAX_FONT_SIZE = 28;

    private string _fontFamilyName = DEFAULT_FONT_FAMILY_NAME;
    private double _fontSize = DEFAULT_FONT_SIZE;

    public string FontFamilyName
    {
        get => _fontFamilyName;
        set => SetProperty(
            ref _fontFamilyName,
            string.IsNullOrWhiteSpace(value) ? DEFAULT_FONT_FAMILY_NAME : value.Trim());
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            var normalizedValue = double.IsFinite(value)
                ? Math.Clamp(value, MIN_FONT_SIZE, MAX_FONT_SIZE)
                : DEFAULT_FONT_SIZE;
            SetProperty(ref _fontSize, normalizedValue);
        }
    }

    public void Reset()
    {
        FontFamilyName = DEFAULT_FONT_FAMILY_NAME;
        FontSize = DEFAULT_FONT_SIZE;
    }
}
