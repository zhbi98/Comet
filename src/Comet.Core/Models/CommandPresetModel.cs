namespace Comet.Models;

/// <summary>
/// Describes a reusable serial payload together with the encoding mode and optional
/// line ending applied when it is sent.
/// </summary>
public sealed class CommandPresetModel
{
    // The UI stores this identifier in button tags so commands remain addressable
    // independently of their current list position or editable display name.
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public required string Name { get; set; }

    public required string Command { get; set; }

    public bool IsHex { get; set; }

    public string LineEnding { get; set; } = "无";

    public string ModeLabel => IsHex ? "HEX 发送" : LineEnding == "无" ? "TEXT" : $"TEXT · {LineEnding}";
}
