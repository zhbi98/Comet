using System.Text;
using Comet.Core.Terminal;
using Comet.Core.Text;
using Comet.Models;

namespace Comet.ViewModels;

/// <summary>
/// Owns the complete terminal session, streaming decoder, and transfer counters.
/// Rendering and viewport state remain in the WinUI terminal control.
/// </summary>
public sealed class TerminalViewModel : ObservableObject
{
    private readonly TerminalBuffer _buffer = new();
    private readonly StreamingTextDecoder _decoder = new();
    private long _totalReceivedBytes;
    private long _totalSentBytes;

    public long TotalReceivedBytes => _totalReceivedBytes;

    public long TotalSentBytes => _totalSentBytes;

    public string ReceiveCountText => $"RX  {FormatByteCount(_totalReceivedBytes)}";

    public string SendCountText => $"TX  {FormatByteCount(_totalSentBytes)}";

    public bool IsEmpty => _buffer.IsEmpty;

    public int SessionLength => _buffer.SessionLength;

    public string SessionText => _buffer.GetSessionText();

    // Decoder state belongs to the active serial connection, while the display buffer
    // can be cleared independently without breaking a split multi-byte character.
    public string DecodeReceived(byte[] data, Encoding encoding) => _decoder.Decode(data, encoding);

    public void ResetDecoder() => _decoder.Reset();

    internal TerminalBufferUpdate Append(
        TerminalEntryModel entry,
        bool shouldIncludeInDisplay,
        bool isReceiveDisplayedAsHex) =>
        _buffer.Append(entry, shouldIncludeInDisplay, isReceiveDisplayedAsHex);

    public void SetReceiveAsHex(bool isReceiveDisplayedAsHex) =>
        _buffer.SetReceiveAsHex(isReceiveDisplayedAsHex);

    public void RecordReceived(int byteCount)
    {
        _totalReceivedBytes += byteCount;
        OnPropertyChanged(nameof(TotalReceivedBytes));
        OnPropertyChanged(nameof(ReceiveCountText));
    }

    public void RecordSent(int byteCount)
    {
        _totalSentBytes += byteCount;
        OnPropertyChanged(nameof(TotalSentBytes));
        OnPropertyChanged(nameof(SendCountText));
    }

    public void Clear()
    {
        // Intentionally keep decoder state. Clearing the terminal is a presentation
        // operation and does not reconnect or reset the serial byte stream.
        _buffer.Clear();
        _totalReceivedBytes = 0;
        _totalSentBytes = 0;
        OnPropertyChanged(nameof(TotalReceivedBytes));
        OnPropertyChanged(nameof(TotalSentBytes));
        OnPropertyChanged(nameof(ReceiveCountText));
        OnPropertyChanged(nameof(SendCountText));
    }

    private static string FormatByteCount(long value) => value switch
    {
        >= 1024 * 1024 => $"{value / 1024d / 1024d:F2} MB",
        >= 1024 => $"{value / 1024d:F2} KB",
        _ => $"{value} B"
    };
}
