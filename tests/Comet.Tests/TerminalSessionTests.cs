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

    private static TerminalEntryModel CreateReceiveEntry(string text, byte[] bytes) => new()
    {
        Time = "12:34:56.789",
        Direction = "RX",
        Text = text,
        IsDetailed = false,
        IsHex = false,
        RawBytes = bytes
    };
}
