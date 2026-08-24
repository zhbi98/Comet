namespace Comet.Models;

public sealed class TerminalEntryModel
{
    public required string Time { get; init; }
    public required string Direction { get; init; }
    public required string Text { get; init; }
    public required bool IsDetailed { get; init; }
    public required bool IsHex { get; init; }
    public byte[]? RawBytes { get; init; }

    public string GetDetailedText(string displayText) => $"{Time}  {Direction,-3}  {displayText}";
}
