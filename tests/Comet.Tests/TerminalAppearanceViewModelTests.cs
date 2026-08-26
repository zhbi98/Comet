using Comet.ViewModels;

namespace Comet.Tests;

[TestClass]
public sealed class TerminalAppearanceViewModelTests
{
    [TestMethod]
    public void FontSize_IsClampedToSupportedRange()
    {
        var viewModel = new TerminalAppearanceViewModel();

        viewModel.FontSize = 1;
        Assert.AreEqual(TerminalAppearanceViewModel.MIN_FONT_SIZE, viewModel.FontSize);

        viewModel.FontSize = 100;
        Assert.AreEqual(TerminalAppearanceViewModel.MAX_FONT_SIZE, viewModel.FontSize);
    }

    [TestMethod]
    public void Reset_RestoresTerminalTypographyDefaults()
    {
        var viewModel = new TerminalAppearanceViewModel
        {
            FontFamilyName = "Consolas",
            FontSize = 20
        };

        viewModel.Reset();

        Assert.AreEqual(TerminalAppearanceViewModel.DEFAULT_FONT_FAMILY_NAME, viewModel.FontFamilyName);
        Assert.AreEqual(TerminalAppearanceViewModel.DEFAULT_FONT_SIZE, viewModel.FontSize);
    }
}
