using System.Text.Json;
using Comet.Core.Presets;
using Comet.Models;
using Comet.Services.Abstractions;
using Comet.ViewModels;

namespace Comet.Tests;

[TestClass]
public sealed class CommandPresetBackupTests
{
    [TestMethod]
    public void JsonCodec_RoundTripsPortablePresetFields()
    {
        var source = new CommandPresetModel
        {
            Name = "查询状态",
            Command = "status\\r",
            IsHex = false,
            LineEnding = "CRLF"
        };

        var json = CommandPresetJsonCodec.Serialize([source]);
        var restored = CommandPresetJsonCodec.Deserialize(json);

        Assert.IsFalse(json.Contains("ModeLabel", StringComparison.Ordinal));
        Assert.HasCount(1, restored);
        Assert.AreEqual(source.Id, restored[0].Id);
        Assert.AreEqual(source.Name, restored[0].Name);
        Assert.AreEqual(source.Command, restored[0].Command);
        Assert.AreEqual(source.IsHex, restored[0].IsHex);
        Assert.AreEqual(source.LineEnding, restored[0].LineEnding);
    }

    [TestMethod]
    public void JsonCodec_NormalizesDuplicateIdentifiersAndUnknownLineEndings()
    {
        const string json = """
            [
              { "Id": "duplicate", "Name": "A", "Command": "one", "LineEnding": "invalid" },
              { "Id": "duplicate", "Name": " ", "Command": "two", "LineEnding": "LF" }
            ]
            """;

        var restored = CommandPresetJsonCodec.Deserialize(json);

        Assert.HasCount(2, restored);
        Assert.AreNotEqual(restored[0].Id, restored[1].Id);
        Assert.AreEqual("无", restored[0].LineEnding);
        Assert.AreEqual("未命名指令", restored[1].Name);
    }

    [TestMethod]
    public void JsonCodec_RejectsEntriesWithoutCommand()
    {
        Assert.ThrowsExactly<JsonException>(() =>
            CommandPresetJsonCodec.Deserialize("[{ \"Name\": \"missing command\" }]"));
    }

    [TestMethod]
    public void ImportBackup_ReplacesCollectionAndPersistsIt()
    {
        var storage = new FakeCommandPresetStorageService();
        var viewModel = new CommandPresetsViewModel(storage);
        viewModel.Add("旧指令", "old", false, "无");
        var backup = CommandPresetJsonCodec.Serialize(
        [
            new CommandPresetModel
            {
                Name = "新指令",
                Command = "AA 55",
                IsHex = true,
                LineEnding = "无"
            }
        ]);

        var importedCount = viewModel.ImportBackup(backup);

        Assert.AreEqual(1, importedCount);
        Assert.HasCount(1, viewModel.Items);
        Assert.AreEqual("新指令", viewModel.Items[0].Name);
        Assert.IsTrue(viewModel.Items[0].IsHex);
        Assert.HasCount(1, storage.LastSavedPresets);
        Assert.AreEqual("新指令", storage.LastSavedPresets[0].Name);
    }

    [TestMethod]
    public void ImportBackup_InvalidJsonLeavesExistingCollectionUnchanged()
    {
        var storage = new FakeCommandPresetStorageService();
        var viewModel = new CommandPresetsViewModel(storage);
        viewModel.Add("保留", "keep", false, "无");

        Assert.ThrowsExactly<JsonException>(() => viewModel.ImportBackup("{ invalid"));

        Assert.HasCount(1, viewModel.Items);
        Assert.AreEqual("保留", viewModel.Items[0].Name);
    }

    [TestMethod]
    public void ImportBackup_SaveFailureRollsBackCollection()
    {
        var storage = new FakeCommandPresetStorageService();
        var viewModel = new CommandPresetsViewModel(storage);
        viewModel.Add("保留", "keep", false, "无");
        storage.ShouldFailSave = true;
        var backup = CommandPresetJsonCodec.Serialize(
        [
            new CommandPresetModel
            {
                Name = "不能保存",
                Command = "new",
                IsHex = false,
                LineEnding = "LF"
            }
        ]);

        Assert.ThrowsExactly<IOException>(() => viewModel.ImportBackup(backup));

        Assert.HasCount(1, viewModel.Items);
        Assert.AreEqual("保留", viewModel.Items[0].Name);
    }

    private sealed class FakeCommandPresetStorageService : ICommandPresetStorageService
    {
        public bool ShouldFailSave { get; set; }

        public IReadOnlyList<CommandPresetModel> LastSavedPresets { get; private set; } = [];

        public IReadOnlyList<CommandPresetModel> LoadPresets() => [];

        public void SavePresets(IEnumerable<CommandPresetModel> presets)
        {
            if (ShouldFailSave)
            {
                throw new IOException("Simulated save failure.");
            }

            LastSavedPresets = presets.ToArray();
        }
    }
}
