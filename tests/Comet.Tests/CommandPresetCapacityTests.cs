using Comet.Core.Presets;
using Comet.Models;
using Comet.ViewModels;

namespace Comet.Tests;

[TestClass]
public sealed class CommandPresetCapacityTests
{
    [TestMethod]
    public void Add_RejectsPresetAfterMaximumCount()
    {
        var viewModel = new CommandPresetsViewModel(new FakeCommandPresetStorageService());
        for (var index = 1; index <= CommandPresetLimits.MaximumCount; index++)
        {
            viewModel.Add($"Preset {index}", index.ToString(), false, "无");
        }

        Assert.IsFalse(viewModel.CanAdd);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            viewModel.Add("Overflow", "61", false, "无"));
        Assert.HasCount(CommandPresetLimits.MaximumCount, viewModel.Items);
    }

    [TestMethod]
    public void Initialize_LoadsMaximumCountWithoutSaving()
    {
        var storage = new FakeCommandPresetStorageService
        {
            InitialPresets = CreatePresets(CommandPresetLimits.MaximumCount + 2)
        };
        var viewModel = new CommandPresetsViewModel(storage);

        viewModel.Initialize();

        Assert.HasCount(CommandPresetLimits.MaximumCount, viewModel.Items);
        Assert.AreEqual($"Preset {CommandPresetLimits.MaximumCount}", viewModel.Items[^1].Name);
        Assert.AreEqual(0, storage.SaveCount);
    }

    private static IReadOnlyList<CommandPresetModel> CreatePresets(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new CommandPresetModel
            {
                Name = $"Preset {index}",
                Command = index.ToString(),
                LineEnding = "无"
            })
            .ToArray();

}
