using System.Text.Json;
using Comet.Models;

namespace Comet.Core.Presets;

/// <summary>
/// Defines the portable JSON representation shared by the local preset store and
/// user-created backup files. File selection and file-system access remain in the UI.
/// </summary>
public static class CommandPresetJsonCodec
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly HashSet<string> _supportedLineEndings =
        new(StringComparer.Ordinal) { "无", "CRLF", "CR", "LF" };

    public static string Serialize(IEnumerable<CommandPresetModel> presets) =>
        JsonSerializer.Serialize(
            presets
                .Take(CommandPresetLimits.MaximumCount)
                .Select(preset => new BackupItem
                {
                    Id = preset.Id,
                    Name = preset.Name,
                    Command = preset.Command,
                    IsHex = preset.IsHex,
                    LineEnding = preset.LineEnding
                }),
            _jsonOptions);

    public static IReadOnlyList<CommandPresetModel> Deserialize(string json)
    {
        var backupItems = JsonSerializer.Deserialize<List<BackupItem>>(json, _jsonOptions)
            ?? throw new JsonException("快捷指令备份必须是 JSON 数组。");

        // Ignore overflow entries instead of rejecting the entire user file. The
        // source file remains untouched until an explicit save action occurs.
        var acceptedItemCount = Math.Min(backupItems.Count, CommandPresetLimits.MaximumCount);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var presets = new List<CommandPresetModel>(acceptedItemCount);
        foreach (var backupItem in backupItems.Take(CommandPresetLimits.MaximumCount))
        {
            if (backupItem is null || backupItem.Command is null)
            {
                throw new JsonException("快捷指令条目缺少 Command 字段。");
            }

            var identifier = NormalizeIdentifier(backupItem.Id, identifiers);
            identifiers.Add(identifier);
            presets.Add(new CommandPresetModel
            {
                Id = identifier,
                Name = string.IsNullOrWhiteSpace(backupItem.Name) ? "未命名指令" : backupItem.Name.Trim(),
                Command = backupItem.Command,
                IsHex = backupItem.IsHex,
                LineEnding = _supportedLineEndings.Contains(backupItem.LineEnding ?? string.Empty)
                    ? backupItem.LineEnding!
                    : "无"
            });
        }

        return presets;
    }

    private static string NormalizeIdentifier(string? identifier, HashSet<string> identifiers)
    {
        if (!string.IsNullOrWhiteSpace(identifier) &&
            identifier.Length <= 128 &&
            !identifiers.Contains(identifier))
        {
            return identifier;
        }

        return Guid.NewGuid().ToString("N");
    }

    private sealed class BackupItem
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? Command { get; init; }

        public bool IsHex { get; init; }

        public string? LineEnding { get; init; }
    }
}
