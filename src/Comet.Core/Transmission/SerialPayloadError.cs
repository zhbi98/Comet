namespace Comet.Core.Transmission;

/// <summary>
/// Identifies validation failures without coupling the core engine to InfoBar titles.
/// </summary>
public enum SerialPayloadErrorKind
{
    None,
    InvalidHex,
    InvalidEscape,
    EmptyPayload
}

/// <summary>
/// Describes a payload validation failure in a presentation-neutral form.
/// </summary>
public sealed record SerialPayloadError(SerialPayloadErrorKind Kind, string Message)
{
    public static SerialPayloadError None { get; } = new(SerialPayloadErrorKind.None, string.Empty);
}
