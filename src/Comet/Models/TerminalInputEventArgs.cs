namespace Comet.Models;

public sealed class TerminalInputEventArgs(string text) : EventArgs
{
    public string Text { get; } = text;
}
