using System.Text.Json;
using Comet.Models;

namespace Comet.Services;

/// <summary>
/// Persists user-defined command presets as JSON in the current user's local
/// application-data directory.
/// </summary>
public static class CommandPresetStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    // Presets are user settings rather than application assets. LocalApplicationData
    // remains writable for installed, unpackaged, and portable application launches.
    private static readonly string StoreDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Comet");

    private static readonly string StorePath = Path.Combine(StoreDirectory, "presets.json");

    public static IReadOnlyList<CommandPresetModel> LoadPresets()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return [];
            }

            var json = File.ReadAllText(StorePath);
            var presets = JsonSerializer.Deserialize<List<CommandPresetModel>>(json, JsonOptions);
            return presets ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A missing, unreadable, or malformed preferences file must not prevent
            // the terminal from starting; the UI falls back to an empty collection.
            return [];
        }
    }

    public static void SavePresets(IEnumerable<CommandPresetModel> presets)
    {
        Directory.CreateDirectory(StoreDirectory);
        var json = JsonSerializer.Serialize(presets, JsonOptions);
        File.WriteAllText(StorePath, json);
    }
}
