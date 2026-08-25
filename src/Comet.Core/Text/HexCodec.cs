using System.Globalization;
using System.Text;

namespace Comet.Core.Text;

public static class HexCodec
{
    public static bool TryParse(string input, out byte[] bytes, out string error)
    {
        var compact = input
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty)
            .Replace("\t", string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Replace(",", string.Empty)
            .Replace(";", string.Empty)
            .Replace(":", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty);

        if (compact.Length == 0)
        {
            bytes = [];
            error = "请输入要发送的十六进制数据。";
            return false;
        }

        if (compact.Length % 2 != 0)
        {
            bytes = [];
            error = "HEX 数据必须由完整字节组成，例如：AA 01 0D 0A。";
            return false;
        }

        bytes = new byte[compact.Length / 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            if (!byte.TryParse(compact.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[index]))
            {
                bytes = [];
                error = $"位置 {index * 2 + 1} 附近包含无效的 HEX 字符。";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public static string Format(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var hex = Convert.ToHexString(bytes);
        var builder = new StringBuilder(hex.Length + bytes.Length - 1);
        for (var index = 0; index < hex.Length; index += 2)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(hex, index, 2);
        }

        return builder.ToString();
    }

}
