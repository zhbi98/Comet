using System.Text;
using Comet.Models;
using Comet.ViewModels;

namespace Comet.Tests;

[TestClass]
public sealed class CommandPresetPayloadTests
{
    [TestMethod]
    public void PrepareCycle_UsesEachPresetModeAndLineEnding()
    {
        var transmission = new TransmissionViewModel();
        CommandPresetModel[] presets =
        [
            new()
            {
                Name = "Text",
                Command = @"help\r",
                IsHex = false,
                LineEnding = "LF"
            },
            new()
            {
                Name = "Hex",
                Command = "31 32 33",
                IsHex = true,
                LineEnding = "CRLF"
            }
        ];

        var success = transmission.TryPrepareCommandPresetCycle(
            presets,
            Encoding.UTF8,
            out var payloads,
            out var error);

        Assert.IsTrue(success);
        Assert.IsNull(error);
        Assert.HasCount(2, payloads);
        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes("help\r\n"),
            payloads[0].Bytes);
        CollectionAssert.AreEqual(
            new byte[] { 0x31, 0x32, 0x33 },
            payloads[1].Bytes);
        Assert.IsFalse(payloads[0].IsHex);
        Assert.IsTrue(payloads[1].IsHex);
    }

    [TestMethod]
    public void PrepareCycle_InvalidPresetRejectsEntireSnapshot()
    {
        var transmission = new TransmissionViewModel();
        CommandPresetModel[] presets =
        [
            new() { Name = "Valid", Command = "status", IsHex = false },
            new() { Name = "Invalid HEX", Command = "GG", IsHex = true }
        ];

        var success = transmission.TryPrepareCommandPresetCycle(
            presets,
            Encoding.UTF8,
            out var payloads,
            out var error);

        Assert.IsFalse(success);
        Assert.IsEmpty(payloads);
        Assert.IsNotNull(error);
        Assert.AreEqual("Invalid HEX", error.PresetName);
    }
}
