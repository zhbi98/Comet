using Comet.Models;

namespace Comet.Services.Abstractions;

/// <summary>
/// Abstracts application preference persistence from view models.
/// </summary>
public interface IAppSettingsStorageService
{
    AppSettingsModel LoadSettings();

    void SaveSettings(AppSettingsModel settings);
}
