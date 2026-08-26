using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Comet.Views;

public sealed partial class MainWindow
{
    private const ulong MAX_PRESET_BACKUP_SIZE_IN_BYTES = 10 * 1024 * 1024;

    private async void ExportCommandPresetsMenuItem_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var picker = CreatePresetExportPicker();
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            await FileIO.WriteTextAsync(
                file,
                _viewModel.CommandPresets.ExportBackup(),
                Windows.Storage.Streams.UnicodeEncoding.Utf8);

            await ShowPresetBackupMessageAsync(
                "导出完成",
                $"已导出 {_viewModel.CommandPresets.Items.Count} 条快捷指令。\n{file.Path}");
        }
        catch (Exception exception)
        {
            await ShowPresetBackupMessageAsync("导出失败", exception.Message);
        }
    }

    private async void ImportCommandPresetsMenuItem_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var picker = CreatePresetImportPicker();
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            await EnsureBackupFileSizeAsync(file);
            var json = await FileIO.ReadTextAsync(file);
            var importCount = _viewModel.CommandPresets.ValidateBackup(json);
            if (!await ConfirmPresetImportAsync(importCount))
            {
                return;
            }

            var importedCount = _viewModel.CommandPresets.ImportBackup(json);
            await ShowPresetBackupMessageAsync("导入完成", $"已恢复 {importedCount} 条快捷指令。");
        }
        catch (Exception exception)
        {
            await ShowPresetBackupMessageAsync("导入失败", exception.Message);
        }
    }

    private FileSavePicker CreatePresetExportPicker()
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"Comet_presets_{DateTime.Now:yyyyMMdd_HHmmss}"
        };
        picker.FileTypeChoices.Add("JSON 备份", [".json"]);
        InitializePickerForWindow(picker);
        return picker;
    }

    private FileOpenPicker CreatePresetImportPicker()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".json");
        InitializePickerForWindow(picker);
        return picker;
    }

    private static async Task EnsureBackupFileSizeAsync(StorageFile file)
    {
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > MAX_PRESET_BACKUP_SIZE_IN_BYTES)
        {
            throw new InvalidDataException("快捷指令备份不能超过 10 MB。");
        }
    }

    private async Task<bool> ConfirmPresetImportAsync(int importCount)
    {
        // Validation is complete before this prompt, so confirmation is only shown
        // for a backup that can be imported.
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = "导入快捷指令",
            Content = $"文件包含 {importCount} 条快捷指令。导入后将替换当前的 " +
                      $"{_viewModel.CommandPresets.Items.Count} 条指令。",
            PrimaryButtonText = "导入并覆盖",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void InitializePickerForWindow(object picker)
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
    }

    private async Task ShowPresetBackupMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "确定"
        };

        await dialog.ShowAsync();
    }
}
