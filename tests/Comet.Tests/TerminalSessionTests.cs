using System.Diagnostics;
using System.Text;
using Comet.Core.Terminal;
using Comet.Core.Text;
using Comet.Models;

namespace Comet.Tests;

[TestClass]
public sealed class TerminalSessionTests
{
    [TestMethod]
    public void ReceiveData_IsAvailableInTextAndSpacedHexViews()
    {
        var buffer = new TerminalBuffer();
        var first = CreateReceiveEntry("123", [0x31, 0x32, 0x33]);

        var textUpdate = buffer.Append(first, shouldIncludeInDisplay: true, isReceiveDisplayedAsHex: false);
        Assert.AreEqual("123", textUpdate.AppendedText);
        Assert.AreEqual("123", buffer.GetSessionText());

        buffer.SetReceiveAsHex(true);
        Assert.AreEqual("31 32 33", buffer.GetSessionText());

        var second = CreateReceiveEntry("4", [0x34]);
        var hexUpdate = buffer.Append(second, shouldIncludeInDisplay: true, isReceiveDisplayedAsHex: true);
        Assert.AreEqual(" 34", hexUpdate.AppendedText);
        Assert.AreEqual("31 32 33 34", buffer.GetSessionText());

        buffer.SetReceiveAsHex(false);
        Assert.AreEqual("1234", buffer.GetSessionText());
    }

    [TestMethod]
    public void StreamingDecoder_PreservesUtf8CharactersAcrossReceiveChunks()
    {
        var decoder = new StreamingTextDecoder();
        var encoding = new UTF8Encoding(false, false);

        Assert.AreEqual(string.Empty, decoder.Decode([0xE4, 0xB8], encoding));
        Assert.AreEqual("中", decoder.Decode([0xAD], encoding));
    }

    [TestMethod]
    public void ReceiveText_CollapsesRedundantCarriageReturnsWithoutRemovingBlankLines()
    {
        var buffer = new TerminalBuffer();

        buffer.Append(
            CreateReceiveEntry("A\r\r\nB\r\n\r\nC", Encoding.ASCII.GetBytes("A\r\r\nB\r\n\r\nC")),
            shouldIncludeInDisplay: true,
            isReceiveDisplayedAsHex: false);

        Assert.AreEqual("A\rB\r\rC", buffer.GetSessionText());
    }

    [TestMethod]
    public void ReceiveText_CollapsesCarriageReturnsSplitAcrossBatches()
    {
        var buffer = new TerminalBuffer();

        buffer.Append(
            CreateReceiveEntry("A\r", [0x41, 0x0D]),
            shouldIncludeInDisplay: true,
            isReceiveDisplayedAsHex: false);
        buffer.Append(
            CreateReceiveEntry("\r\nB", [0x0D, 0x0A, 0x42]),
            shouldIncludeInDisplay: true,
            isReceiveDisplayedAsHex: false);

        Assert.AreEqual("A\rB", buffer.GetSessionText());
    }

    [TestMethod]
    public void ReceiveGrouping_StartsNewGroupAtIdleThreshold()
    {
        const long first = 1_000_000;
        var belowThreshold = AddElapsed(first, TerminalReceiveGrouping.IdleThreshold - TimeSpan.FromMilliseconds(1));
        var atThreshold = AddElapsed(first, TerminalReceiveGrouping.IdleThreshold);

        Assert.IsTrue(TerminalReceiveGrouping.StartsNewGroup(null, first));
        Assert.IsFalse(TerminalReceiveGrouping.StartsNewGroup(first, belowThreshold));
        Assert.IsTrue(TerminalReceiveGrouping.StartsNewGroup(first, atThreshold));
        Assert.IsTrue(TerminalReceiveGrouping.StartsNewGroup(first, first - 1));
    }

    [TestMethod]
    public void DetailedReceive_AddsNewTimestampAfterIdleGroupBoundaryInBothViews()
    {
        var buffer = new TerminalBuffer();
        var first = CreateReceiveEntry(
            "A",
            [0x41],
            isDetailed: true,
            time: "12:34:56.000",
            startsNewReceiveGroup: true);
        var second = CreateReceiveEntry(
            "B",
            [0x42],
            isDetailed: true,
            time: "12:34:56.100");
        var third = CreateReceiveEntry(
            "C",
            [0x43],
            isDetailed: true,
            time: "12:34:56.600",
            startsNewReceiveGroup: true);

        buffer.Append(first, shouldIncludeInDisplay: true, isReceiveDisplayedAsHex: false);
        buffer.Append(second, shouldIncludeInDisplay: true, isReceiveDisplayedAsHex: false);
        var thirdUpdate = buffer.Append(third, shouldIncludeInDisplay: true, isReceiveDisplayedAsHex: false);

        Assert.AreEqual(Environment.NewLine + third.GetDetailedText("C"), thirdUpdate.AppendedText);
        Assert.AreEqual(
            first.GetDetailedText("A") + "B" + Environment.NewLine + third.GetDetailedText("C"),
            buffer.GetSessionText());

        buffer.SetReceiveAsHex(true);
        Assert.AreEqual(
            first.GetDetailedText("41") + " 42" + Environment.NewLine + third.GetDetailedText("43"),
            buffer.GetSessionText());
    }

    [TestMethod]
    public void DetailedText_UsesTwoSpacesBetweenDirectionAndContent()
    {
        foreach (var direction in new[] { "RX", "TX", "SYS" })
        {
            var entry = new TerminalEntryModel
            {
                Time = "23:06:48.925",
                Direction = direction,
                Text = "INFO",
                IsDetailed = true,
                IsHex = false
            };

            Assert.AreEqual($"23:06:48.925  {direction}  INFO", entry.GetDetailedText("INFO"));
        }
    }

    private static TerminalEntryModel CreateReceiveEntry(
        string text,
        byte[] bytes,
        bool isDetailed = false,
        string time = "12:34:56.789",
        bool startsNewReceiveGroup = false) => new()
    {
        Time = time,
        Direction = "RX",
        Text = text,
        IsDetailed = isDetailed,
        IsHex = false,
        RawBytes = bytes,
        StartsNewReceiveGroup = startsNewReceiveGroup
    };

    private static long AddElapsed(long timestamp, TimeSpan elapsed) =>
        timestamp + checked((long)Math.Ceiling(elapsed.TotalSeconds * Stopwatch.Frequency));
}
