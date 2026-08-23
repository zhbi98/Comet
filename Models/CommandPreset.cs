namespace Comet.Models;

public sealed class CommandPreset
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public required string Name { get; set; }

    public required string Command { get; set; }

    public bool IsHex { get; set; }

    public string LineEnding { get; set; } = "无";

    public string ModeLabel => IsHex ? "HEX 发送" : LineEnding == "无" ? "TEXT" : $"TEXT · {LineEnding}";
}
