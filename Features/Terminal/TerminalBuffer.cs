using System.Text;
using Comet.Models;

namespace Comet.Features.Terminal;

/// <summary>
/// Owns terminal history and the bounded text snapshot shown by the view.
/// </summary>
internal sealed class TerminalBuffer(int maxEntries, int maxCharacters)
{
    private readonly Queue<TerminalEntry> _history = new(maxEntries);
    private readonly StringBuilder _text = new(Math.Min(maxCharacters, 64 * 1024));
    private bool _hasDisplayedEntry;
    private bool _lastEntryWasDetailed;
    private bool _lastEntryWasHex;
    private string? _lastDetailedDirection;

    public bool IsEmpty => _text.Length == 0;

    public string GetText() => _text.ToString();

    public bool Append(TerminalEntry entry, bool includeInDisplay)
    {
        _history.Enqueue(entry);
        while (_history.Count > maxEntries)
        {
            _history.Dequeue();
        }

        if (!includeInDisplay)
        {
            return false;
        }

        if (_hasDisplayedEntry && _lastEntryWasHex != entry.IsHex)
        {
            EnsureLineBoundary();
        }

        if (!entry.IsDetailed)
        {
            if (_lastEntryWasDetailed)
            {
                EnsureLineBoundary();
            }

            AppendHexSeparatorIfNeeded(entry);
            _text.Append(entry.Text);
        }
        else if (_lastEntryWasDetailed &&
                 _lastDetailedDirection == "RX" &&
                 entry.Direction == "RX" &&
                 _lastEntryWasHex == entry.IsHex)
        {
            // Serial receive events are arbitrary transport chunks, not lines.
            // Consecutive RX chunks must remain a single continuous stream.
            AppendHexSeparatorIfNeeded(entry);
            _text.Append(entry.Text);
        }
        else
        {
            EnsureLineBoundary();
            _text.Append(entry.DetailedText);
        }

        _hasDisplayedEntry = true;
        _lastEntryWasDetailed = entry.IsDetailed;
        _lastEntryWasHex = entry.IsHex;
        _lastDetailedDirection = entry.IsDetailed ? entry.Direction : null;
        TrimToCapacity();
        return true;
    }

    public void Clear()
    {
        _history.Clear();
        _text.Clear();
        _hasDisplayedEntry = false;
        _lastEntryWasDetailed = false;
        _lastEntryWasHex = false;
        _lastDetailedDirection = null;
    }

    private void AppendHexSeparatorIfNeeded(TerminalEntry entry)
    {
        if (!entry.IsHex || !_lastEntryWasHex || _text.Length == 0 || entry.Text.Length == 0)
        {
            return;
        }

        if (!char.IsWhiteSpace(_text[^1]) && !char.IsWhiteSpace(entry.Text[0]))
        {
            _text.Append(' ');
        }
    }

    private void EnsureLineBoundary()
    {
        if (_text.Length > 0 && _text[^1] is not '\r' and not '\n')
        {
            _text.AppendLine();
        }
    }

    private void TrimToCapacity()
    {
        if (_text.Length <= maxCharacters)
        {
            return;
        }

        var minimumRemoveCount = _text.Length - maxCharacters;
        var removeCount = minimumRemoveCount;
        while (removeCount < _text.Length && _text[removeCount] != '\n')
        {
            removeCount++;
        }

        if (removeCount < _text.Length)
        {
            removeCount++;
        }
        else
        {
            removeCount = minimumRemoveCount;
        }

        _text.Remove(0, removeCount);
    }
}
