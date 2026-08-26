using Comet.Models;
using Comet.Services.Abstractions;

namespace Comet.Tests;

/// <summary>
/// Records preset persistence calls without touching the user's configuration file.
/// </summary>
internal sealed class FakeCommandPresetStorageService : ICommandPresetStorageService
{
    public bool ShouldFailSave { get; set; }

    public IReadOnlyList<CommandPresetModel> InitialPresets { get; init; } = [];

    public IReadOnlyList<CommandPresetModel> LastSavedPresets { get; private set; } = [];

    public int SaveCount { get; private set; }

    public IReadOnlyList<CommandPresetModel> LoadPresets() => InitialPresets;

    public void SavePresets(IEnumerable<CommandPresetModel> presets)
    {
        SaveCount++;
        if (ShouldFailSave)
        {
            throw new IOException("Simulated save failure.");
        }

        LastSavedPresets = presets.ToArray();
    }
}
