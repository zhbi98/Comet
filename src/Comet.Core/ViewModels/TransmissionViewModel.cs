using System.Text;
using Comet.Core.Text;
using Comet.Core.Transmission;
using Comet.Models;

namespace Comet.ViewModels;

public sealed record CommandPresetPayloadError(string PresetName, SerialPayloadError Error);

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

    public bool TryPrepareCommandPresetCycle(
        IEnumerable<CommandPresetModel> presets,
        Encoding encoding,
        out IReadOnlyList<PreparedSerialPayload> payloads,
        out CommandPresetPayloadError? error)
    {
        var preparedPayloads = new List<PreparedSerialPayload>();
        foreach (var preset in presets)
        {
            if (!SerialPayloadEngine.TryPrepareComposerPayload(
                    preset.Command,
                    preset.IsHex,
                    preset.LineEnding,
                    encoding,
                    out var payload,
                    out var payloadError))
            {
                payloads = [];
                error = new CommandPresetPayloadError(preset.Name, payloadError);
                return false;
            }

            preparedPayloads.Add(payload);
        }

        payloads = preparedPayloads;
        error = null;
        return true;
    }
}
