using System.Text;
using Comet.Models;
using Comet.Utilities;

namespace Comet.Features.Terminal;

internal readonly record struct TerminalBufferUpdate(
    bool HasChange,
    bool RequiresFullRender,
    string AppendedText)
{
    public static TerminalBufferUpdate None => new(false, false, string.Empty);
}

/// <summary>
/// Owns synchronized text and HEX snapshots of the bounded terminal content.
/// </summary>
internal sealed class TerminalBuffer(int maxCharacters)
{
    private readonly DisplayState _textDisplay = new(maxCharacters);
    private readonly DisplayState _hexDisplay = new(maxCharacters);
    private bool _receiveAsHex;

    private DisplayState CurrentDisplay => _receiveAsHex ? _hexDisplay : _textDisplay;

    public bool IsEmpty => CurrentDisplay.IsEmpty;

    public int CurrentLength => CurrentDisplay.Length;

    public string GetText() => CurrentDisplay.GetText();

    public TerminalBufferUpdate Append(TerminalEntry entry, bool includeInDisplay, bool receiveAsHex)
    {
        _receiveAsHex = receiveAsHex;
        if (!includeInDisplay)
        {
            return TerminalBufferUpdate.None;
        }

        var textUpdate = _textDisplay.Append(entry, receiveAsHex: false);
        var hexUpdate = _hexDisplay.Append(entry, receiveAsHex: true);
        return receiveAsHex ? hexUpdate : textUpdate;
    }

    public void SetReceiveAsHex(bool receiveAsHex) => _receiveAsHex = receiveAsHex;

    public void Clear()
    {
        _textDisplay.Clear();
        _hexDisplay.Clear();
    }

    private sealed class DisplayState(int maxCharacters)
    {
        private readonly StringBuilder _text = new(Math.Min(maxCharacters, 64 * 1024));
        private bool _hasDisplayedEntry;
        private bool _lastEntryWasDetailed;
        private bool _lastEntryWasHex;
        private string? _lastDetailedDirection;

        public bool IsEmpty => _text.Length == 0;

        public int Length => _text.Length;

        public string GetText() => _text.ToString();

        public TerminalBufferUpdate Append(TerminalEntry entry, bool receiveAsHex)
        {
            var previousLength = _text.Length;
            var displayText = GetDisplayText(entry, receiveAsHex);
            var isDisplayedAsHex = IsDisplayedAsHex(entry, receiveAsHex);

            if (_hasDisplayedEntry && _lastEntryWasHex != isDisplayedAsHex)
            {
                EnsureLineBoundary();
            }

            if (!entry.IsDetailed)
            {
                if (_lastEntryWasDetailed)
                {
                    EnsureLineBoundary();
                }

                AppendHexSeparatorIfNeeded(displayText, isDisplayedAsHex);
                _text.Append(displayText);
            }
            else if (_lastEntryWasDetailed &&
                     _lastDetailedDirection == "RX" &&
                     entry.Direction == "RX" &&
                     _lastEntryWasHex == isDisplayedAsHex)
            {
                // Serial receive events are arbitrary transport chunks, not lines.
                // Consecutive RX chunks must remain a single continuous stream.
                AppendHexSeparatorIfNeeded(displayText, isDisplayedAsHex);
                _text.Append(displayText);
            }
            else
            {
                EnsureLineBoundary();
                _text.Append(entry.GetDetailedText(displayText));
            }

            _hasDisplayedEntry = true;
            _lastEntryWasDetailed = entry.IsDetailed;
            _lastEntryWasHex = isDisplayedAsHex;
            _lastDetailedDirection = entry.IsDetailed ? entry.Direction : null;
            if (TrimToCapacity(receiveAsHex))
            {
                return new TerminalBufferUpdate(true, true, string.Empty);
            }

            return new TerminalBufferUpdate(
                true,
                false,
                _text.ToString(previousLength, _text.Length - previousLength));
        }

        public void Clear()
        {
            _text.Clear();
            _hasDisplayedEntry = false;
            _lastEntryWasDetailed = false;
            _lastEntryWasHex = false;
            _lastDetailedDirection = null;
        }

        private static string GetDisplayText(TerminalEntry entry, bool receiveAsHex) =>
            entry.Direction == "RX" && entry.RawBytes is not null && receiveAsHex
                ? HexCodec.Format(entry.RawBytes)
                : entry.Text;

        private static bool IsDisplayedAsHex(TerminalEntry entry, bool receiveAsHex) =>
            entry.Direction == "RX" && entry.RawBytes is not null
                ? receiveAsHex
                : entry.IsHex;

        private void AppendHexSeparatorIfNeeded(string displayText, bool isDisplayedAsHex)
        {
            if (!isDisplayedAsHex || !_lastEntryWasHex || _text.Length == 0 || displayText.Length == 0)
            {
                return;
            }

            if (!char.IsWhiteSpace(_text[^1]) && !char.IsWhiteSpace(displayText[0]))
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

        private bool TrimToCapacity(bool receiveAsHex)
        {
            if (_text.Length <= maxCharacters)
            {
                return false;
            }

            // Leave headroom instead of shifting a nearly full StringBuilder for
            // every incoming serial packet. At the production limit this removes
            // about 12.5% in one operation and makes trimming infrequent.
            var desiredHeadroom = Math.Clamp(maxCharacters / 8, 16 * 1024, 128 * 1024);
            var headroom = Math.Min(desiredHeadroom, Math.Max(1, maxCharacters / 2));
            var targetRemoveCount = Math.Min(
                _text.Length,
                _text.Length - maxCharacters + headroom);

            // Prefer a nearby line boundary, but never scan the remainder of a
            // megabyte-long stream when the data contains no line breaks.
            const int maximumBoundarySearch = 4096;
            var searchEnd = Math.Min(_text.Length, targetRemoveCount + maximumBoundarySearch);
            var lineBoundary = targetRemoveCount;
            while (lineBoundary < searchEnd && _text[lineBoundary] != '\n')
            {
                lineBoundary++;
            }

            int removeCount;
            if (lineBoundary < searchEnd)
            {
                removeCount = lineBoundary + 1;
            }
            else if (receiveAsHex)
            {
                // A continuous HEX stream has separators but usually no line breaks.
                removeCount = targetRemoveCount;
                while (removeCount < searchEnd && !char.IsWhiteSpace(_text[removeCount]))
                {
                    removeCount++;
                }

                while (removeCount < searchEnd && char.IsWhiteSpace(_text[removeCount]))
                {
                    removeCount++;
                }

                if (removeCount == searchEnd)
                {
                    removeCount = targetRemoveCount;
                }
            }
            else
            {
                removeCount = targetRemoveCount;
                if (removeCount < _text.Length &&
                    removeCount > 0 &&
                    char.IsLowSurrogate(_text[removeCount]) &&
                    char.IsHighSurrogate(_text[removeCount - 1]))
                {
                    removeCount++;
                }
            }

            _text.Remove(0, removeCount);
            return true;
        }
    }
}
