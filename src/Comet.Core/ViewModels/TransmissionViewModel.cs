using System.Text;
using Comet.Core.Text;
using Comet.Core.Transmission;

namespace Comet.ViewModels;

/// <summary>
/// Exposes the transmission engine to the presentation layer without coupling
/// payload rules to WinUI controls.
/// </summary>
public sealed class TransmissionViewModel
{
    public Encoding GetEncoding(string? name) => TextEncodingCatalog.Get(name);

    public bool TryPrepareComposerPayload(
        string content,
        bool isHex,
        string? lineEnding,
        Encoding encoding,
        out PreparedSerialPayload payload,
        out SerialPayloadError error) =>
        SerialPayloadEngine.TryPrepareComposerPayload(
            content,
            isHex,
            lineEnding,
            encoding,
            out payload,
            out error);

    public byte[] PrepareTerminalInput(string text, string? lineEnding, Encoding encoding) =>
        SerialPayloadEngine.PrepareTerminalInput(text, lineEnding, encoding);
}
