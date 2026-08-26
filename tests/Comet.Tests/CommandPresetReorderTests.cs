using Comet.ViewModels;

namespace Comet.Tests;

[TestClass]
public sealed class CommandPresetReorderTests
{
    [TestMethod]
    public void SaveOrder_PersistsCurrentCollectionOrder()
    {
        var storage = new FakeCommandPresetStorageService();
        var viewModel = CreateViewModel(storage);

        viewModel.Items.Move(0, 2);
        viewModel.SaveOrder();

        CollectionAssert.AreEqual(
            new[] { "Second", "Third", "First" },
            storage.LastSavedPresets.Select(preset => preset.Name).ToArray());
    }

    [TestMethod]
    public void Reorder_DoesNotPersistBeforeSaveOrder()
    {
        var storage = new FakeCommandPresetStorageService();
        var viewModel = CreateViewModel(storage);
        var saveCountBeforeReorder = storage.SaveCount;

        viewModel.Items.Move(0, 2);

        Assert.AreEqual(saveCountBeforeReorder, storage.SaveCount);
    }

    [TestMethod]
    public void SaveOrder_SaveFailureKeepsCurrentOrder()
    {
        var storage = new FakeCommandPresetStorageService();
        var viewModel = CreateViewModel(storage);
        storage.ShouldFailSave = true;
        viewModel.Items.Move(0, 2);

        Assert.ThrowsExactly<IOException>(viewModel.SaveOrder);
        CollectionAssert.AreEqual(
            new[] { "Second", "Third", "First" },
            viewModel.Items.Select(preset => preset.Name).ToArray());
    }

    private static CommandPresetsViewModel CreateViewModel(FakeCommandPresetStorageService storage)
    {
        var viewModel = new CommandPresetsViewModel(storage);
        viewModel.Add("First", "1", false, "无");
        viewModel.Add("Second", "2", false, "无");
        viewModel.Add("Third", "3", false, "无");
        return viewModel;
    }

}
