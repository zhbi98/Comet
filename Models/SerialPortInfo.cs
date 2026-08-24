namespace Comet.Models;

public sealed record SerialPortInfo(string PortName, string? FriendlyName)
{
    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName)
        ? PortName
        : $"{PortName} ({FriendlyName})";

    public override string ToString() => DisplayName;
}
