using Comet.Models;

namespace Comet.Services.Abstractions;

/// <summary>
/// Abstracts user preset persistence so the view model does not know the storage
/// format or the platform-specific settings location.
/// </summary>
public interface ICommandPresetStorageService
{
    IReadOnlyList<CommandPresetModel> LoadPresets();

    void SavePresets(IEnumerable<CommandPresetModel> presets);
}
