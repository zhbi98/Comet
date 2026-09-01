namespace Comet.Models;

/// <summary>
/// Stores user preferences that should survive application restarts.
/// Runtime state such as active connections, recording sessions, and terminal
/// content intentionally stays outside this model.
/// </summary>
public sealed class AppSettingsModel
{
    /// <summary>
    /// Terminal rendering preferences.
    /// </summary>
    public TerminalDisplaySettingsModel Terminal { get; set; } = new();

    /// <summary>
    /// Last-used serial connection options.
    /// </summary>
    public SerialSettingsModel Serial { get; set; } = new();

    /// <summary>
    /// Bottom composer preferences.
    /// </summary>
    public SendSettingsModel Send { get; set; } = new();
}

public sealed class TerminalDisplaySettingsModel
{
    public string FontFamilyName { get; set; } = "Cascadia Mono";

    public double FontSize { get; set; } = 13;

    public bool ReceiveAsHex { get; set; }

    public bool TimestampEnabled { get; set; } = true;

    public bool AutoScrollEnabled { get; set; } = true;
}

public sealed class SerialSettingsModel
{
    public const int DEFAULT_BAUD_RATE = 115200;
    public const int DEFAULT_DATA_BITS = 8;
    public const string DEFAULT_ENCODING_NAME = "UTF-8";
    public const string DEFAULT_STOP_BITS = "1";
    public const string DEFAULT_PARITY = "None";
    public const string DEFAULT_HANDSHAKE = "None";

    public string? LastPortName { get; set; }

    public int BaudRate { get; set; } = DEFAULT_BAUD_RATE;

    public string EncodingName { get; set; } = DEFAULT_ENCODING_NAME;

    public int DataBits { get; set; } = DEFAULT_DATA_BITS;

    public string StopBits { get; set; } = DEFAULT_STOP_BITS;

    public string Parity { get; set; } = DEFAULT_PARITY;

    public string Handshake { get; set; } = DEFAULT_HANDSHAKE;
}

public sealed class SendSettingsModel
{
    public const string DEFAULT_LINE_ENDING = "无";
    public const double DEFAULT_REPEAT_INTERVAL_MS = 1000;

    public bool IsHex { get; set; }

    public string LineEnding { get; set; } = DEFAULT_LINE_ENDING;

    public double RepeatIntervalMs { get; set; } = DEFAULT_REPEAT_INTERVAL_MS;
}
