namespace Comet.Core.Transmission;

/// <summary>
/// Contains the immutable bytes to write and the text recorded for a successful TX.
/// </summary>
public sealed record PreparedSerialPayload(byte[] Bytes, string DisplayText, bool IsHex);
