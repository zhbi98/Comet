using System.Text.Json;
using Comet.Models;
using Comet.Services.Abstractions;

namespace Comet.Services;

/// <summary>
/// Persists application preferences as JSON in the current user's local data folder.
/// </summary>
public sealed class AppSettingsStorageService : IAppSettingsStorageService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string _storeDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Comet");

    private static readonly string _storePath = Path.Combine(_storeDirectory, "settings.json");

    public AppSettingsModel LoadSettings()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                return new AppSettingsModel();
            }

            var settings = JsonSerializer.Deserialize<AppSettingsModel>(
                File.ReadAllText(_storePath),
                _jsonOptions);
            return NormalizeSettings(settings);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Preference loading should never block the terminal from opening.
            return new AppSettingsModel();
        }
    }

    public void SaveSettings(AppSettingsModel settings)
    {
        Directory.CreateDirectory(_storeDirectory);
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        var temporaryPath = Path.Combine(_storeDirectory, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
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
            // A stale temporary file is harmless; keep the original save error visible.
        }
    }

    private static AppSettingsModel NormalizeSettings(AppSettingsModel? settings)
    {
        settings ??= new AppSettingsModel();
        settings.Terminal ??= new TerminalDisplaySettingsModel();
        settings.Serial ??= new SerialSettingsModel();
        settings.Send ??= new SendSettingsModel();
        return settings;
    }
}
