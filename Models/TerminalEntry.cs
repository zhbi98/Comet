namespace Comet.Models;

public sealed class TerminalEntry
{
    public required string Time { get; init; }
    public required string Direction { get; init; }
    public required string Text { get; init; }
    public required bool IsDetailed { get; init; }
    public required bool IsHex { get; init; }

    public string DetailedText => $"{Time}  {Direction,-3}  {Text}";
}
