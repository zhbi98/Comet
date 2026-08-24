namespace Comet.Core.Terminal;

internal readonly record struct TerminalBufferUpdate(
    bool HasChange,
    int RemovedPrefixLength,
    string AppendedText)
{
    public static TerminalBufferUpdate None => new(false, 0, string.Empty);
}
