using System.Collections.ObjectModel;
using Comet.Core.Presets;
using Comet.Models;
using Comet.Services.Abstractions;

namespace Comet.ViewModels;

/// <summary>
/// Coordinates editable command presets independently of their ListView presentation.
/// </summary>
public sealed class CommandPresetsViewModel : ObservableObject
{
    private readonly ICommandPresetStorageService _storageService;

    public CommandPresetsViewModel(ICommandPresetStorageService storageService)
    {
        _storageService = storageService;
    }

    public ObservableCollection<CommandPresetModel> Items { get; } = [];

    public string CountText => $"{Items.Count} 条预设";

    public bool IsEmpty => Items.Count == 0;

    public void Initialize()
    {
        Items.Clear();
        foreach (var preset in _storageService.LoadPresets())
        {
            Items.Add(preset);
        }

        // Saving immediately preserves the existing behavior: a missing preferences
        // file is created on first launch and malformed input becomes an empty store.
        Save();
        RaiseCollectionStateChanged();
    }

    public CommandPresetModel Add(string? name, string command, bool isHex, string lineEnding)
    {
        // The fallback number is based on the current UI order, matching the original
        // command panel behavior after deletions and additions.
        var preset = new CommandPresetModel
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"指令 {Items.Count + 1}" : name.Trim(),
            Command = command,
            IsHex = isHex,
            LineEnding = lineEnding
        };
        Items.Add(preset);
        Save();
        RaiseCollectionStateChanged();
        return preset;
    }

    public void Delete(CommandPresetModel preset)
    {
        if (!Items.Remove(preset))
        {
            return;
        }

        Save();
        RaiseCollectionStateChanged();
    }

    public void UpdateName(CommandPresetModel preset, string? name)
    {
        preset.Name = string.IsNullOrWhiteSpace(name) ? "未命名指令" : name.Trim();
        Save();
    }

    public void UpdateCommand(CommandPresetModel preset, string command)
    {
        preset.Command = command;
        Save();
    }

    public CommandPresetModel? Find(string id) => Items.FirstOrDefault(preset => preset.Id == id);

    public string ExportBackup() => CommandPresetJsonCodec.Serialize(Items);

    public int ValidateBackup(string json) => CommandPresetJsonCodec.Deserialize(json).Count;

    public int ImportBackup(string json)
    {
        // Parse and normalize the complete backup before touching the observable
        // collection. Invalid JSON therefore cannot partially replace the UI state.
        var importedPresets = CommandPresetJsonCodec.Deserialize(json);
        var previousPresets = Items.ToArray();
        ReplaceItems(importedPresets);
        try
        {
            Save();
            return importedPresets.Count;
        }
        catch
        {
            // Keep the in-memory collection consistent with the last known state when
            // persistence fails. The original exception is retained for the UI.
            ReplaceItems(previousPresets);
            throw;
        }
    }

    private void Save() => _storageService.SavePresets(Items);

    private void RaiseCollectionStateChanged()
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void ReplaceItems(IEnumerable<CommandPresetModel> presets)
    {
        Items.Clear();
        foreach (var preset in presets)
        {
            Items.Add(preset);
        }

        RaiseCollectionStateChanged();
    }
}
