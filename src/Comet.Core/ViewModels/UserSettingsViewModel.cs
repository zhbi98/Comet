using Comet.Models;
using Comet.Services.Abstractions;

namespace Comet.ViewModels;

/// <summary>
/// Coordinates restart-persistent preferences without exposing storage details to views.
/// </summary>
public sealed class UserSettingsViewModel
{
    private readonly IAppSettingsStorageService _storageService;

    public UserSettingsViewModel(IAppSettingsStorageService storageService)
    {
        _storageService = storageService;
        Current = _storageService.LoadSettings();
    }

    public AppSettingsModel Current { get; private set; }

    public void Save(AppSettingsModel settings)
    {
        Current = settings;
        _storageService.SaveSettings(settings);
    }
}
