using System.Text.Json;
using Comet.Core.Presets;
using Comet.Models;
using Comet.Services.Abstractions;

namespace Comet.Services;

/// <summary>
/// Persists user-defined command presets as JSON in the current user's local
/// application-data directory.
/// </summary>
public sealed class CommandPresetStorageService : ICommandPresetStorageService
{
    // Presets are user settings rather than application assets. LocalApplicationData
    // remains writable for installed, unpackaged, and portable application launches.
    private static readonly string _storeDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Comet");

    private static readonly string _storePath = Path.Combine(_storeDirectory, "presets.json");

    public IReadOnlyList<CommandPresetModel> LoadPresets()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                return [];
            }

            return CommandPresetJsonCodec.Deserialize(File.ReadAllText(_storePath));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A missing, unreadable, or malformed preferences file must not prevent
            // the terminal from starting; the UI falls back to an empty collection.
            return [];
        }
    }

    public void SavePresets(IEnumerable<CommandPresetModel> presets)
    {
        Directory.CreateDirectory(_storeDirectory);
        var json = CommandPresetJsonCodec.Serialize(presets);
        var temporaryPath = Path.Combine(_storeDirectory, $"presets.{Guid.NewGuid():N}.tmp");
        try
        {
            // Write the complete replacement first. The previous preferences file
            // remains intact if serialization or the temporary write fails.
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _storePath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A stale temporary file is harmless and must not hide the original
            // persistence error from the caller.
        }
    }
}
