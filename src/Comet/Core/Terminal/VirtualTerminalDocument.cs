using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Comet.Core.Terminal;

/// <summary>
/// Describes one visual row while keeping its positions in the original UTF-16 document.
/// <see cref="Text"/> may expand tabs for display, so it must not be used as the source for copying.
/// </summary>
internal sealed record TerminalDisplayLine(
    int Start,
    int Length,
    int BreakLength,
    string Text,
    int CellCount)
{
    public int End => Start + Length;
}

/// <summary>
/// Maintains a UTF-16 terminal document and a fixed-cell wrapped line index.
/// Appends only rebuild the previous partial display line and the new suffix.
/// </summary>
internal sealed class VirtualTerminalDocument
{
    private const int TAB_SIZE = 4;

    private readonly StringBuilder _text = new();
    private ObservableCollection<TerminalDisplayLine> _lines = [];
    private int _columns = 80;

    public int CharacterCount => _text.Length;

    public int LineCount => _lines.Count;

    public IList<TerminalDisplayLine> Lines => _lines;

    public void Clear()
    {
        _text.Clear();
        _lines = [new TerminalDisplayLine(0, 0, 0, string.Empty, 0)];
    }

    public void SetText(string text)
    {
        _text.Clear();
        _text.Append(text);
        RebuildAllLines();
    }

    public void Append(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        // New characters can only affect the previous tail row: it may gain text,
        // complete a CRLF pair, or wrap into additional rows. Earlier rows are immutable.
        var rebuildIndex = Math.Max(0, _lines.Count - 1);
        var rebuildStart = _lines.Count == 0 ? 0 : _lines[rebuildIndex].Start;
        _text.Append(text);
        var replacement = CreateLines(rebuildStart);
        if (_lines.Count == 0)
        {
            foreach (var line in replacement)
            {
                _lines.Add(line);
            }

            return;
        }

        // Replace and Add notifications let ItemsRepeater preserve its realized window;
        // a Reset here would recycle every visible element for each receive batch.
        _lines[rebuildIndex] = replacement[0];
        for (var index = 1; index < replacement.Count; index++)
        {
            _lines.Add(replacement[index]);
        }
    }

    public bool SetColumns(int columns)
    {
        columns = Math.Max(1, columns);
        if (_columns == columns)
        {
            return false;
        }

        _columns = columns;
        RebuildAllLines();
        return true;
    }

    public TerminalDisplayLine GetLine(int index) => _lines[Math.Clamp(index, 0, _lines.Count - 1)];

    public int FindLineIndex(int documentOffset)
    {
        if (_lines.Count == 0)
        {
            return 0;
        }

        documentOffset = Math.Clamp(documentOffset, 0, _text.Length);
        // Find the last row whose start is not greater than the document offset.
        // This also assigns an offset at a soft-wrap boundary to the following row.
        var low = 0;
        var high = _lines.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (_lines[middle].Start <= documentOffset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Math.Clamp(high, 0, _lines.Count - 1);
    }

    public int GetDocumentOffset(int lineIndex, double cellPosition)
    {
        var line = GetLine(lineIndex);
        // Pointer coordinates use terminal cells, whereas selections use UTF-16 offsets.
        // Choosing the nearest side of a wide glyph makes click placement predictable.
        var targetCell = Math.Max(0, (int)Math.Round(cellPosition, MidpointRounding.AwayFromZero));
        var offset = line.Start;
        var cell = 0;
        while (offset < line.End)
        {
            var codeUnitLength = GetCodeUnitLength(offset);
            var width = GetCellWidth(offset, cell);
            if (targetCell < cell + Math.Max(1, width))
            {
                return targetCell - cell >= Math.Max(1, width) / 2.0
                    ? offset + codeUnitLength
                    : offset;
            }

            cell += width;
            offset += codeUnitLength;
        }

        return line.End;
    }

    public int GetCellOffset(TerminalDisplayLine line, int documentOffset)
    {
        documentOffset = Math.Clamp(documentOffset, line.Start, line.End);
        var offset = line.Start;
        var cell = 0;
        while (offset < documentOffset)
        {
            var codeUnitLength = GetCodeUnitLength(offset);
            if (offset + codeUnitLength > documentOffset)
            {
                break;
            }

            cell += GetCellWidth(offset, cell);
            offset += codeUnitLength;
        }

        return cell;
    }

    public string GetText(int start, int length)
    {
        start = Math.Clamp(start, 0, _text.Length);
        length = Math.Clamp(length, 0, _text.Length - start);
        return length == 0 ? string.Empty : _text.ToString(start, length);
    }

    public int MoveByCodePoint(int position, int delta)
    {
        // Selection positions are UTF-16 offsets, but keyboard navigation must not
        // leave the caret between the high and low surrogate of one Unicode scalar.
        position = Math.Clamp(position, 0, _text.Length);
        if (delta < 0 && position > 0)
        {
            position--;
            if (position > 0 && char.IsLowSurrogate(_text[position]) && char.IsHighSurrogate(_text[position - 1]))
            {
                position--;
            }
        }
        else if (delta > 0 && position < _text.Length)
        {
            if (char.IsHighSurrogate(_text[position]) &&
                position + 1 < _text.Length &&
                char.IsLowSurrogate(_text[position + 1]))
            {
                position += 2;
            }
            else
            {
                position++;
            }
        }

        return position;
    }

    private void RebuildAllLines()
    {
        // A complete replacement is reserved for mode or width changes, where every
        // row may legitimately differ and incremental notifications provide no benefit.
        _lines = new ObservableCollection<TerminalDisplayLine>(CreateLines(0));
    }

    private List<TerminalDisplayLine> CreateLines(int start)
    {
        var result = new List<TerminalDisplayLine>();
        if (_text.Length == 0)
        {
            result.Add(new TerminalDisplayLine(0, 0, 0, string.Empty, 0));
            return result;
        }

        var position = Math.Clamp(start, 0, _text.Length);
        if (position == _text.Length)
        {
            result.Add(new TerminalDisplayLine(position, 0, 0, string.Empty, 0));
            return result;
        }

        // A row ends at either a hard line break or the configured cell width.
        // The source length and rendered cell count deliberately remain separate.
        while (position < _text.Length)
        {
            var lineStart = position;
            var cellCount = 0;
            var display = new StringBuilder();
            var breakLength = 0;

            while (position < _text.Length)
            {
                var character = _text[position];
                if (character is '\r' or '\n')
                {
                    breakLength = character == '\r' &&
                                  position + 1 < _text.Length &&
                                  _text[position + 1] == '\n'
                        ? 2
                        : 1;
                    position += breakLength;
                    break;
                }

                var codeUnitLength = GetCodeUnitLength(position);
                var cellWidth = GetCellWidth(position, cellCount);
                if (cellCount > 0 && cellCount + cellWidth > _columns)
                {
                    break;
                }

                if (character == '\t')
                {
                    // Expanding tabs only in the row text keeps measurement simple;
                    // copying still reads the original tab from the document.
                    display.Append(' ', cellWidth);
                }
                else
                {
                    display.Append(_text[position]);
                    if (codeUnitLength == 2)
                    {
                        display.Append(_text[position + 1]);
                    }
                }

                position += codeUnitLength;
                cellCount += cellWidth;
                if (cellCount >= _columns)
                {
                    break;
                }
            }

            var lineLength = position - lineStart - breakLength;
            result.Add(new TerminalDisplayLine(lineStart, lineLength, breakLength, display.ToString(), cellCount));

            if (breakLength > 0 && position == _text.Length)
            {
                result.Add(new TerminalDisplayLine(position, 0, 0, string.Empty, 0));
            }
        }

        return result;
    }

    private int GetCodeUnitLength(int offset) =>
        char.IsHighSurrogate(_text[offset]) &&
        offset + 1 < _text.Length &&
        char.IsLowSurrogate(_text[offset + 1])
            ? 2
            : 1;

    private int GetCellWidth(int offset, int currentCell)
    {
        if (_text[offset] == '\t')
        {
            return TAB_SIZE - (currentCell % TAB_SIZE);
        }

        // This is a terminal-oriented width approximation rather than full text shaping.
        // It covers combining marks, common CJK ranges, and emoji used in serial output.
        var codeUnitLength = GetCodeUnitLength(offset);
        var category = codeUnitLength == 1
            ? char.GetUnicodeCategory(_text[offset])
            : UnicodeCategory.OtherSymbol;
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
        {
            return 0;
        }

        var codePoint = codeUnitLength == 2
            ? char.ConvertToUtf32(_text[offset], _text[offset + 1])
            : _text[offset];
        return IsWideCodePoint(codePoint) ? 2 : 1;
    }

    private static bool IsWideCodePoint(int value) =>
        value is >= 0x1100 and <= 0x115F or
        >= 0x2329 and <= 0x232A or
        >= 0x2E80 and <= 0xA4CF or
        >= 0xAC00 and <= 0xD7A3 or
        >= 0xF900 and <= 0xFAFF or
        >= 0xFE10 and <= 0xFE19 or
        >= 0xFE30 and <= 0xFE6F or
        >= 0xFF00 and <= 0xFF60 or
        >= 0xFFE0 and <= 0xFFE6 or
        >= 0x1F300 and <= 0x1FAFF or
        >= 0x20000 and <= 0x3FFFD;

}
