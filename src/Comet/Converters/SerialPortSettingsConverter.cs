using System.IO.Ports;

namespace Comet.Converters;

internal static class SerialPortSettingsConverter
{
    public static Parity ParseParity(string? value) => value switch
    {
        "Odd" => Parity.Odd,
        "Even" => Parity.Even,
        "Mark" => Parity.Mark,
        "Space" => Parity.Space,
        _ => Parity.None
    };

    public static StopBits ParseStopBits(string? value) => value switch
    {
        "1.5" => StopBits.OnePointFive,
        "2" => StopBits.Two,
        _ => StopBits.One
    };

    public static Handshake ParseHandshake(string? value) => value switch
    {
        "XOn/XOff" => Handshake.XOnXOff,
        "RTS/CTS" => Handshake.RequestToSend,
        "RTS/CTS + XOn/XOff" => Handshake.RequestToSendXOnXOff,
        _ => Handshake.None
    };

    public static string GetParityShortName(Parity parity) => parity switch
    {
        Parity.Odd => "O",
        Parity.Even => "E",
        Parity.Mark => "M",
        Parity.Space => "S",
        _ => "N"
    };

    public static string GetStopBitsShortName(StopBits stopBits) => stopBits switch
    {
        StopBits.OnePointFive => "1.5",
        StopBits.Two => "2",
        _ => "1"
    };
}
