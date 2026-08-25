using System.Text;
using Comet.Core.Transmission;

namespace Comet.Tests;

[TestClass]
public sealed class SerialPayloadEngineTests
{
    [TestMethod]
    public void ComposerText_InterpretsEscapesWithoutAddingADefaultLineEnding()
    {
        var succeeded = SerialPayloadEngine.TryPrepareComposerPayload(
            "help\\r",
            isHex: false,
            lineEnding: "无",
            Encoding.UTF8,
            out var payload,
            out var error);

        Assert.IsTrue(succeeded, error.Message);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("help\r"), payload.Bytes);
        Assert.AreEqual("help\r", payload.DisplayText);
        Assert.IsFalse(payload.IsHex);
    }

    [TestMethod]
    public void ComposerHex_ProducesRawBytesAndSpacedDisplayText()
    {
        var succeeded = SerialPayloadEngine.TryPrepareComposerPayload(
            "31,32 33",
            isHex: true,
            lineEnding: "CRLF",
            Encoding.UTF8,
            out var payload,
            out var error);

        Assert.IsTrue(succeeded, error.Message);
        CollectionAssert.AreEqual(new byte[] { 0x31, 0x32, 0x33 }, payload.Bytes);
        Assert.AreEqual("31 32 33", payload.DisplayText);
    }

    [TestMethod]
    public void TerminalEnter_UsesLfWhenComposerLineEndingIsNone()
    {
        var payload = SerialPayloadEngine.PrepareTerminalInput("help\r\n", "无", Encoding.UTF8);

        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("help\n"), payload);
    }
}
