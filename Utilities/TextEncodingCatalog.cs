using System.Text;

namespace Comet.Utilities;

internal static class TextEncodingCatalog
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, false);
    private static readonly Encoding Ascii = Encoding.ASCII;
    private static readonly Lazy<Encoding> Gbk = new(() => Encoding.GetEncoding(936));

    public static Encoding Get(string? name) => name switch
    {
        "GBK" => Gbk.Value,
        "ASCII" => Ascii,
        _ => Utf8
    };
}
