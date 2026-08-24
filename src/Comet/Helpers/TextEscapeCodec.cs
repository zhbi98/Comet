using System.Globalization;
using System.Text;

namespace Comet.Helpers;

internal static class TextEscapeCodec
{
    public static bool TryDecode(string value, out string decoded, out string error)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '\\' || index == value.Length - 1)
            {
                builder.Append(character);
                continue;
            }

            var escape = value[++index];
            switch (escape)
            {
                case '\\': builder.Append('\\'); break;
                case '0': builder.Append('\0'); break;
                case 'a': builder.Append('\a'); break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case 'v': builder.Append('\v'); break;
                case 'x':
                    if (!TryReadHex(value, index + 1, 2, out var byteValue))
                    {
                        decoded = string.Empty;
                        error = "\\x 后必须跟随两位十六进制数，例如 \\x0D。";
                        return false;
                    }

                    builder.Append((char)byteValue);
                    index += 2;
                    break;
                case 'u':
                    if (!TryReadHex(value, index + 1, 4, out var unicodeValue))
                    {
                        decoded = string.Empty;
                        error = "\\u 后必须跟随四位十六进制数，例如 \\u4F60。";
                        return false;
                    }

                    builder.Append((char)unicodeValue);
                    index += 4;
                    break;
                default:
                    builder.Append('\\').Append(escape);
                    break;
            }
        }

        decoded = builder.ToString();
        error = string.Empty;
        return true;
    }

    private static bool TryReadHex(string value, int start, int length, out int result)
    {
        result = 0;
        if (start + length > value.Length)
        {
            return false;
        }

        return int.TryParse(
            value.AsSpan(start, length),
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out result);
    }
}
