namespace Comet.Core.Terminal;

internal readonly record struct TerminalBufferUpdate(
    bool HasChange,
    string AppendedText)
{
    public static TerminalBufferUpdate None => new(false, string.Empty);
}
