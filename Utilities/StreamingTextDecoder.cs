using System.Text;

namespace Comet.Utilities;

/// <summary>
/// Preserves decoder state across arbitrary serial chunks. Bytes that cannot be
/// decoded by the selected encoding use a compact visible replacement character.
/// </summary>
internal sealed class StreamingTextDecoder
{
    private readonly char[] _characters = new char[8192];
    private Decoder? _decoder;
    private int _codePage = -1;

    public string Decode(byte[] bytes, Encoding encoding)
    {
        EnsureDecoder(encoding);
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var decoded = new StringBuilder(bytes.Length);
        var offset = 0;
        while (offset < bytes.Length)
        {
            _decoder!.Convert(
                bytes,
                offset,
                bytes.Length - offset,
                _characters,
                0,
                _characters.Length,
                flush: false,
                out var bytesUsed,
                out var charactersUsed,
                out _);

            if (charactersUsed > 0)
            {
                decoded.Append(_characters, 0, charactersUsed);
            }

            if (bytesUsed == 0 && charactersUsed == 0)
            {
                throw new InvalidOperationException("The streaming decoder did not make progress.");
            }

            offset += bytesUsed;
        }

        return decoded.ToString();
    }

    public void Reset()
    {
        _decoder?.Reset();
        _decoder = null;
        _codePage = -1;
    }

    private void EnsureDecoder(Encoding encoding)
    {
        if (_decoder is not null && _codePage == encoding.CodePage)
        {
            return;
        }

        var displayEncoding = (Encoding)encoding.Clone();
        // A plain question mark avoids expensive font fallback when a binary stream
        // contains many invalid sequences, while still presenting ordinary garbled
        // text instead of expanding every source byte to a \xNN escape.
        displayEncoding.DecoderFallback = new DecoderReplacementFallback("?");
        _decoder = displayEncoding.GetDecoder();
        _codePage = encoding.CodePage;
    }
}
