using System.Text;
using Comet.Models;
using Comet.Utilities;

namespace Comet.Features.Terminal;

internal readonly record struct TerminalBufferUpdate(
    bool HasChange,
    int RemovedPrefixLength,
    string AppendedText)
{
    public static TerminalBufferUpdate None => new(false, 0, string.Empty);
}

/// <summary>
/// Owns synchronized text and HEX segmented ring buffers for terminal content.
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
        private readonly SegmentedTextRing _text = new();
        private bool _hasDisplayedEntry;
        private bool _lastEntryWasDetailed;
        private bool _lastEntryWasHex;
        private string? _lastDetailedDirection;
        private bool _previousRxTextEndedWithCarriageReturn;

        public bool IsEmpty => _text.Length == 0;

        public int Length => _text.Length;

        public string GetText() => _text.GetText();

        public TerminalBufferUpdate Append(TerminalEntry entry, bool receiveAsHex)
        {
            var appended = new StringBuilder();
            var displayText = GetDisplayText(entry, receiveAsHex);
            var isDisplayedAsHex = IsDisplayedAsHex(entry, receiveAsHex);

            if (_hasDisplayedEntry && _lastEntryWasHex != isDisplayedAsHex)
            {
                EnsureLineBoundary(appended);
            }

            if (!entry.IsDetailed)
            {
                if (_lastEntryWasDetailed)
                {
                    EnsureLineBoundary(appended);
                }

                AppendHexSeparatorIfNeeded(appended, displayText, isDisplayedAsHex);
                appended.Append(displayText);
            }
            else if (_lastEntryWasDetailed &&
                     _lastDetailedDirection == "RX" &&
                     entry.Direction == "RX" &&
                     _lastEntryWasHex == isDisplayedAsHex)
            {
                // Serial receive events are arbitrary transport chunks, not lines.
                // Consecutive RX chunks must remain a single continuous stream.
                AppendHexSeparatorIfNeeded(appended, displayText, isDisplayedAsHex);
                appended.Append(displayText);
            }
            else
            {
                EnsureLineBoundary(appended);
                appended.Append(entry.GetDetailedText(displayText));
            }

            _hasDisplayedEntry = true;
            _lastEntryWasDetailed = entry.IsDetailed;
            _lastEntryWasHex = isDisplayedAsHex;
            _lastDetailedDirection = entry.IsDetailed ? entry.Direction : null;
            var appendedText = appended.ToString();
            _text.Append(appendedText);
            var removedPrefixLength = TrimToCapacity(receiveAsHex);

            return new TerminalBufferUpdate(
                true,
                removedPrefixLength,
                appendedText);
        }

        public void Clear()
        {
            _text.Clear();
            _hasDisplayedEntry = false;
            _lastEntryWasDetailed = false;
            _lastEntryWasHex = false;
            _lastDetailedDirection = null;
            _previousRxTextEndedWithCarriageReturn = false;
        }

        private string GetDisplayText(TerminalEntry entry, bool receiveAsHex)
        {
            if (entry.Direction == "RX" && entry.RawBytes is not null)
            {
                return receiveAsHex
                    ? HexCodec.Format(entry.RawBytes)
                    : NormalizeTextBoxText(entry.Text, ref _previousRxTextEndedWithCarriageReturn);
            }

            var previousWasCarriageReturn = false;
            return NormalizeTextBoxText(entry.Text, ref previousWasCarriageReturn);
        }

        private static string NormalizeTextBoxText(string text, ref bool previousWasCarriageReturn)
        {
            if (text.Length == 0)
            {
                return string.Empty;
            }

            // WinUI TextBox terminates inserted text at NUL and normalizes all line
            // endings to CR. Canonicalize before buffering so ring offsets always
            // match the characters actually retained by the control.
            var normalized = new StringBuilder(text.Length);
            foreach (var character in text)
            {
                if (character == '\n')
                {
                    if (!previousWasCarriageReturn)
                    {
                        normalized.Append('\r');
                    }

                    previousWasCarriageReturn = false;
                    continue;
                }

                if (character == '\r')
                {
                    normalized.Append('\r');
                    previousWasCarriageReturn = true;
                    continue;
                }

                if (character == '\t')
                {
                    normalized.Append('\t');
                    previousWasCarriageReturn = false;
                    continue;
                }

                if (character <= '\u001F')
                {
                    normalized.Append((char)('\u2400' + character));
                }
                else if (character == '\u007F')
                {
                    normalized.Append('\u2421');
                }
                else if (char.IsControl(character))
                {
                    normalized.Append("\\u");
                    normalized.Append(((int)character).ToString("X4"));
                }
                else
                {
                    normalized.Append(character);
                }
                previousWasCarriageReturn = false;
            }

            return normalized.ToString();
        }

        private static bool IsDisplayedAsHex(TerminalEntry entry, bool receiveAsHex) =>
            entry.Direction == "RX" && entry.RawBytes is not null
                ? receiveAsHex
                : entry.IsHex;

        private void AppendHexSeparatorIfNeeded(StringBuilder appended, string displayText, bool isDisplayedAsHex)
        {
            if (!isDisplayedAsHex || !_lastEntryWasHex ||
                _text.Length + appended.Length == 0 || displayText.Length == 0)
            {
                return;
            }

            var lastCharacter = appended.Length > 0 ? appended[^1] : _text.LastCharacter;
            if (!char.IsWhiteSpace(lastCharacter) && !char.IsWhiteSpace(displayText[0]))
            {
                appended.Append(' ');
            }
        }

        private void EnsureLineBoundary(StringBuilder appended)
        {
            if (_text.Length + appended.Length == 0)
            {
                return;
            }

            var lastCharacter = appended.Length > 0 ? appended[^1] : _text.LastCharacter;
            if (lastCharacter is not '\r' and not '\n')
            {
                appended.AppendLine();
            }
        }

        private int TrimToCapacity(bool receiveAsHex)
        {
            if (_text.Length <= maxCharacters)
            {
                return 0;
            }

            // Keep the visible amount stable at the rolling limit. The previous
            // headroom trim removed roughly 12.5% at once and forced a full TextBox
            // reset, which looked like data loss and reset the scroll layout.
            var removeCount = _text.Length - maxCharacters;
            var lastRemovedCharacter = _text.RemovePrefix(removeCount);
            if (receiveAsHex)
            {
                // Move by at most one byte token so HEX never starts halfway
                // through a two-digit value.
                if (lastRemovedCharacter is not null && !char.IsWhiteSpace(lastRemovedCharacter.Value))
                {
                    while (!_text.IsEmpty && !char.IsWhiteSpace(_text.FirstCharacter))
                    {
                        _text.RemovePrefix(1);
                        removeCount++;
                    }
                }

                while (!_text.IsEmpty && char.IsWhiteSpace(_text.FirstCharacter))
                {
                    _text.RemovePrefix(1);
                    removeCount++;
                }
            }
            else if (!_text.IsEmpty && char.IsLowSurrogate(_text.FirstCharacter))
            {
                _text.RemovePrefix(1);
                removeCount++;
            }

            return removeCount;
        }
    }

    /// <summary>
    /// A FIFO of immutable text segments. Prefix trimming advances the first
    /// segment offset or unlinks complete segments, so it never moves the
    /// remaining million-character payload.
    /// </summary>
    private sealed class SegmentedTextRing
    {
        private readonly LinkedList<Segment> _segments = [];

        public bool IsEmpty => Length == 0;

        public int Length { get; private set; }

        public char FirstCharacter
        {
            get
            {
                var first = _segments.First?.Value ?? throw new InvalidOperationException("The ring is empty.");
                return first.Text[first.Offset];
            }
        }

        public char LastCharacter
        {
            get
            {
                var last = _segments.Last?.Value ?? throw new InvalidOperationException("The ring is empty.");
                return last.Text[^1];
            }
        }

        public void Append(string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            _segments.AddLast(new Segment(text));
            Length += text.Length;
        }

        public char? RemovePrefix(int count)
        {
            if (count <= 0 || Length == 0)
            {
                return null;
            }

            count = Math.Min(count, Length);
            char? lastRemovedCharacter = null;
            while (count > 0)
            {
                var node = _segments.First!;
                var segment = node.Value;
                var available = segment.Text.Length - segment.Offset;
                var removeFromSegment = Math.Min(count, available);
                lastRemovedCharacter = segment.Text[segment.Offset + removeFromSegment - 1];
                segment.Offset += removeFromSegment;
                Length -= removeFromSegment;
                count -= removeFromSegment;

                if (segment.Offset == segment.Text.Length)
                {
                    _segments.RemoveFirst();
                }
            }

            return lastRemovedCharacter;
        }

        public string GetText()
        {
            if (Length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(Length);
            foreach (var segment in _segments)
            {
                builder.Append(segment.Text.AsSpan(segment.Offset));
            }

            return builder.ToString();
        }

        public void Clear()
        {
            _segments.Clear();
            Length = 0;
        }

        private sealed class Segment(string text)
        {
            public string Text { get; } = text;

            public int Offset { get; set; }
        }
    }
}
