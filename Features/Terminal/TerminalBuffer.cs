using System.Text;
using Comet.Models;
using Comet.Utilities;

namespace Comet.Features.Terminal;

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

    public string GetText() => CurrentDisplay.GetText();

    public bool Append(TerminalEntry entry, bool includeInDisplay, bool receiveAsHex)
    {
        _receiveAsHex = receiveAsHex;
        if (!includeInDisplay)
        {
            return false;
        }

        _textDisplay.Append(entry, receiveAsHex: false);
        _hexDisplay.Append(entry, receiveAsHex: true);
        return true;
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

        public string GetText() => _text.ToString();

        public void Append(TerminalEntry entry, bool receiveAsHex)
        {
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
            TrimToCapacity(receiveAsHex);
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

        private void TrimToCapacity(bool receiveAsHex)
        {
            if (_text.Length <= maxCharacters)
            {
                return;
            }

            var minimumRemoveCount = _text.Length - maxCharacters;
            var lineBoundary = minimumRemoveCount;
            while (lineBoundary < _text.Length && _text[lineBoundary] != '\n')
            {
                lineBoundary++;
            }

            int removeCount;
            if (lineBoundary < _text.Length)
            {
                removeCount = lineBoundary + 1;
            }
            else if (receiveAsHex)
            {
                // A continuous HEX stream may not contain line breaks. Move the cut
                // to the next byte separator so the visible text never starts mid-byte.
                removeCount = minimumRemoveCount;
                while (removeCount < _text.Length && !char.IsWhiteSpace(_text[removeCount]))
                {
                    removeCount++;
                }

                while (removeCount < _text.Length && char.IsWhiteSpace(_text[removeCount]))
                {
                    removeCount++;
                }

                if (removeCount == _text.Length)
                {
                    removeCount = minimumRemoveCount;
                }
            }
            else
            {
                removeCount = minimumRemoveCount;
            }

            _text.Remove(0, removeCount);
        }
    }
}
