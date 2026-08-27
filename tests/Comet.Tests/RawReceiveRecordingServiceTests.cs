using System.Security.Cryptography;
using Comet.Recording;
using Comet.ViewModels;

namespace Comet.Tests;

[TestClass]
public sealed class RawReceiveRecordingServiceTests
{
    [TestMethod]
    public async Task Recording_WritesOnlyBuffersSubmittedWhileActive()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"Comet-{Guid.NewGuid():N}.bin");
        try
        {
            var service = new RawReceiveRecordingService();
            using var viewModel = new ReceiveRecordingViewModel(service);

            Assert.IsFalse(viewModel.TryRecord([0xAA]));
            viewModel.Start(filePath);
            Assert.IsTrue(viewModel.TryRecord([0x00, 0xFF, 0x31]));
            Assert.IsTrue(viewModel.TryRecord([0x0D, 0x0A, 0x80]));
            await viewModel.StopAsync();
            Assert.IsFalse(viewModel.TryRecord([0xBB]));

            CollectionAssert.AreEqual(
                new byte[] { 0x00, 0xFF, 0x31, 0x0D, 0x0A, 0x80 },
                await File.ReadAllBytesAsync(filePath));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public async Task Recording_PreservesLargeBinaryStream()
    {
        const int chunkSize = 16 * 1024;
        const int chunkCount = 128;
        var filePath = Path.Combine(Path.GetTempPath(), $"Comet-{Guid.NewGuid():N}.bin");
        var expected = new byte[chunkSize * chunkCount];
        Random.Shared.NextBytes(expected);

        try
        {
            using var service = new RawReceiveRecordingService();
            service.Start(filePath);
            for (var offset = 0; offset < expected.Length; offset += chunkSize)
            {
                Assert.IsTrue(service.TryWrite(expected[offset..(offset + chunkSize)]));
            }

            await service.StopAsync();
            var actual = await File.ReadAllBytesAsync(filePath);

            Assert.AreEqual(expected.Length, actual.Length);
            CollectionAssert.AreEqual(SHA256.HashData(expected), SHA256.HashData(actual));
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
