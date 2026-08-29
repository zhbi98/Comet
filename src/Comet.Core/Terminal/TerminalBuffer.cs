using System.Text;
using Comet.Core.Text;
using Comet.Models;

namespace Comet.Core.Terminal;

/// <summary>
/// Owns synchronized, complete text and HEX formatted terminal sessions.
/// </summary>
internal sealed class TerminalBuffer
{
    private readonly DisplayState _textDisplay = new();
    private readonly DisplayState _hexDisplay = new();
    private bool _isReceiveDisplayedAsHex;

    private DisplayState CurrentDisplay => _isReceiveDisplayedAsHex ? _hexDisplay : _textDisplay;

    public bool IsEmpty => CurrentDisplay.IsEmpty;

    public int CurrentLength => CurrentDisplay.Length;

    public int SessionLength => CurrentDisplay.Length;

    public string GetText() => CurrentDisplay.GetText();

    public string GetSessionText() => CurrentDisplay.GetText();

    public TerminalBufferUpdate Append(TerminalEntryModel entry, bool shouldIncludeInDisplay, bool isReceiveDisplayedAsHex)
    {
        _isReceiveDisplayedAsHex = isReceiveDisplayedAsHex;
        if (!shouldIncludeInDisplay)
        {
            return TerminalBufferUpdate.None;
        }

        // Build both views at receive time so switching display modes never needs the
        // original byte batches or a conversion from the currently visible document.
        var textUpdate = _textDisplay.Append(entry, isReceiveDisplayedAsHex: false);
        var hexUpdate = _hexDisplay.Append(entry, isReceiveDisplayedAsHex: true);
        return isReceiveDisplayedAsHex ? hexUpdate : textUpdate;
    }

    public void SetReceiveAsHex(bool isReceiveDisplayedAsHex) => _isReceiveDisplayedAsHex = isReceiveDisplayedAsHex;

    public void Clear()
    {
        _textDisplay.Clear();
        _hexDisplay.Clear();
    }

    private sealed class DisplayState
    {
        private readonly SegmentedTextStore _sessionText = new();
        // Formatting state spans transport batches because one terminal line may arrive
        // through several DataReceived callbacks.
        private bool _hasDisplayedEntry;
        private bool _lastEntryWasDetailed;
        private bool _lastEntryWasHex;
        private string? _lastDetailedDirection;
        private bool _previousRxTextEndedWithCarriageReturn;

        public bool IsEmpty => _sessionText.Length == 0;

        public int Length => _sessionText.Length;

        public string GetText() => _sessionText.GetText();

        public TerminalBufferUpdate Append(TerminalEntryModel entry, bool isReceiveDisplayedAsHex)
        {
            var appended = new StringBuilder();
            var displayText = GetDisplayText(entry, isReceiveDisplayedAsHex);
            var isDisplayedAsHex = IsDisplayedAsHex(entry, isReceiveDisplayedAsHex);

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
                // Transport chunks are not terminal lines. Consecutive RX chunks
                // remain one stream until the direction or display mode changes.
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
            _sessionText.Append(appendedText);

            return new TerminalBufferUpdate(true, appendedText);
        }

        public void Clear()
        {
            _sessionText.Clear();
            _hasDisplayedEntry = false;
            _lastEntryWasDetailed = false;
            _lastEntryWasHex = false;
            _lastDetailedDirection = null;
            _previousRxTextEndedWithCarriageReturn = false;
        }

        private string GetDisplayText(TerminalEntryModel entry, bool isReceiveDisplayedAsHex)
        {
            if (entry.Direction == "RX" && entry.RawBytes is not null)
            {
                return isReceiveDisplayedAsHex
                    ? HexCodec.Format(entry.RawBytes)
                    : NormalizeTerminalText(entry.Text, ref _previousRxTextEndedWithCarriageReturn);
            }

            var previousWasCarriageReturn = false;
            return NormalizeTerminalText(entry.Text, ref previousWasCarriageReturn);
        }

        private static string NormalizeTerminalText(string text, ref bool previousWasCarriageReturn)
        {
            if (text.Length == 0)
            {
                return string.Empty;
            }

            // Keep one canonical line-break character and render non-layout C0
            // controls as visible glyphs so logical positions stay stable. The CR
            // state is carried across calls because CRLF may be split between batches.
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
                    // Some bootloaders emit CRCRLF ("\r\r\n"). The first CR only
                    // returns the cursor to column zero; treating both CR characters
                    // as line breaks creates a misleading blank row. Collapse repeated
                    // CR characters, including when the sequence is split across
                    // receive batches. A real CRLFCRLF sequence still contains two
                    // line breaks because each CR is separated from the next by LF.
                    if (!previousWasCarriageReturn)
                    {
                        normalized.Append('\r');
                    }

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

        private static bool IsDisplayedAsHex(TerminalEntryModel entry, bool isReceiveDisplayedAsHex) =>
            entry.Direction == "RX" && entry.RawBytes is not null
                ? isReceiveDisplayedAsHex
                : entry.IsHex;

        private void AppendHexSeparatorIfNeeded(StringBuilder appended, string displayText, bool isDisplayedAsHex)
        {
            if (!isDisplayedAsHex || !_lastEntryWasHex ||
                _sessionText.Length + appended.Length == 0 || displayText.Length == 0)
            {
                return;
            }

            // Consecutive byte batches must remain tokenized as "AA BB", even though
            // their formatting is produced by separate Append calls.
            var lastCharacter = appended.Length > 0 ? appended[^1] : _sessionText.LastCharacter;
            if (!char.IsWhiteSpace(lastCharacter) && !char.IsWhiteSpace(displayText[0]))
            {
                appended.Append(' ');
            }
        }

        private void EnsureLineBoundary(StringBuilder appended)
        {
            if (_sessionText.Length + appended.Length == 0)
            {
                return;
            }

            var lastCharacter = appended.Length > 0 ? appended[^1] : _sessionText.LastCharacter;
            if (lastCharacter is not '\r' and not '\n')
            {
                appended.AppendLine();
            }
        }
    }

    /// <summary>
    /// Stores immutable append segments without copying the existing session.
    /// Materialization only happens for export or a display-mode switch.
    /// </summary>
    private sealed class SegmentedTextStore
    {
        private readonly LinkedList<string> _segments = [];

        public int Length { get; private set; }

        public char LastCharacter => _segments.Last?.Value[^1]
            ?? throw new InvalidOperationException("The session is empty.");

        public void Append(string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            _segments.AddLast(text);
            Length += text.Length;
        }

        public string GetText()
        {
            if (Length == 0)
            {
                return string.Empty;
            }

            // Full materialization is intentionally limited to display-mode switches
            // and log export; ordinary appends never copy the existing session.
            var builder = new StringBuilder(Length);
            foreach (var segment in _segments)
            {
                builder.Append(segment);
            }

            return builder.ToString();
        }

        public void Clear()
        {
            _segments.Clear();
            Length = 0;
        }
    }
}
