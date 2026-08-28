using System.IO.Ports;
using Comet.Models;
using Comet.Services.Abstractions;
using Comet.ViewModels;

namespace Comet.Tests;

[TestClass]
public sealed class ConnectionViewModelTests
{
    [TestMethod]
    public void MissingPhysicalPort_KeepsSessionActiveUntilExplicitClose()
    {
        var serialPort = new FakeSerialPortService();
        using var viewModel = new ConnectionViewModel(serialPort);
        viewModel.Open(new SerialPortConnectionOptions(
            "COM5",
            115200,
            8,
            Parity.None,
            StopBits.One,
            Handshake.None,
            false,
            false));
        serialPort.IsOpen = false;

        Assert.IsFalse(viewModel.IsConnected);
        Assert.IsTrue(viewModel.IsConnectionActive);
        Assert.AreEqual("COM5", viewModel.PortName);

        serialPort.IsOpen = true;

        Assert.IsTrue(viewModel.IsConnected);
        Assert.AreEqual("COM5", viewModel.Close());
        Assert.IsFalse(viewModel.IsConnectionActive);
    }

    private sealed class FakeSerialPortService : ISerialPortService
    {
        private string? _portName;

        public event EventHandler<SerialBytesReceivedEventArgs>? BytesReceived
        {
            add { }
            remove { }
        }

        public event Action<string>? ErrorOccurred
        {
            add { }
            remove { }
        }

        public bool IsOpen { get; set; }

        public bool IsConnectionActive => _portName is not null;

        public string? PortName => _portName;

        public IReadOnlyList<SerialPortInfoModel> GetAvailablePorts() => [];

        public void Open(SerialPortConnectionOptions options)
        {
            _portName = options.PortName;
            IsOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
            _portName = null;
        }

        public void Send(byte[] data)
        {
        }

        public void Dispose()
        {
        }
    }
}
