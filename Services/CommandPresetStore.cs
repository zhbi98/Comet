using System.Text.Json;
using Comet.Models;

namespace Comet.Services;

public static class CommandPresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string StoreDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Comet");

    private static readonly string StorePath = Path.Combine(StoreDirectory, "presets.json");

    public static IReadOnlyList<CommandPreset> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return [];
            }

            var json = File.ReadAllText(StorePath);
            var presets = JsonSerializer.Deserialize<List<CommandPreset>>(json, JsonOptions);
            return presets ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<CommandPreset> presets)
    {
        Directory.CreateDirectory(StoreDirectory);
        var json = JsonSerializer.Serialize(presets, JsonOptions);
        File.WriteAllText(StorePath, json);
    }
}
