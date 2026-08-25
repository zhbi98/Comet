using System.Text;
using Comet.Core.Text;

namespace Comet.Core.Transmission;

/// <summary>
/// Converts user-facing composer and terminal input into serial payloads without
/// depending on WinUI controls or a physical serial port.
/// </summary>
public static class SerialPayloadEngine
{
    public static bool TryPrepareComposerPayload(
        string content,
        bool isHex,
        string? lineEnding,
        Encoding encoding,
        out PreparedSerialPayload payload,
        out SerialPayloadError error)
    {
        if (isHex)
        {
            // Line-ending options apply only to text payloads. HEX input already
            // expresses every byte explicitly, including 0D and 0A.
            if (!HexCodec.TryParse(content, out var bytes, out var hexError))
            {
                payload = null!;
                error = new SerialPayloadError(SerialPayloadErrorKind.InvalidHex, hexError);
                return false;
            }

            payload = new PreparedSerialPayload(bytes, HexCodec.Format(bytes), true);
            error = SerialPayloadError.None;
            return true;
        }

        if (!TextEscapeCodec.TryDecode(content, out var decodedText, out var escapeError))
        {
            payload = null!;
            error = new SerialPayloadError(SerialPayloadErrorKind.InvalidEscape, escapeError);
            return false;
        }

        var text = decodedText + ResolveLineEnding(lineEnding);
        if (text.Length == 0)
        {
            payload = null!;
            error = new SerialPayloadError(
                SerialPayloadErrorKind.EmptyPayload,
                "请输入文本或选择一个行尾符。");
            return false;
        }

        payload = new PreparedSerialPayload(encoding.GetBytes(text), text, false);
        error = SerialPayloadError.None;
        return true;
    }

    public static byte[] PrepareTerminalInput(string text, string? lineEnding, Encoding encoding)
    {
        // Terminal input contains real newline characters rather than composer escape
        // syntax. With no configured ending, Enter still emits the conventional LF.
        var configuredLineEnding = ResolveLineEnding(lineEnding);
        var terminalLineEnding = configuredLineEnding.Length == 0 ? "\n" : configuredLineEnding;
        var normalizedText = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\n", terminalLineEnding);
        return encoding.GetBytes(normalizedText);
    }

    public static string ResolveLineEnding(string? lineEnding) => lineEnding switch
    {
        "CRLF" => "\r\n",
        "CR" => "\r",
        "LF" => "\n",
        _ => string.Empty
    };
}
