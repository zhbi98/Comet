using Comet.Core.Transmission;
using Comet.Models;
using Comet.Services.Abstractions;
using Comet.ViewModels;

namespace Comet.Tests;

[TestClass]
public sealed class RepeatSendViewModelTests
{
    [TestMethod]
    public void Start_WaitsForTheFirstTimerTickBeforeSending()
    {
        var serialPort = new FakeSerialPortService { IsOpen = true };
        using var connection = new ConnectionViewModel(serialPort);
        FakePeriodicTimer? timer = null;
        using var repeat = new RepeatSendViewModel(
            connection,
            callback => timer = new FakePeriodicTimer(callback));
        var payload = new PreparedSerialPayload([0x41], "A", false);
        var sentEvents = 0;
        repeat.PayloadSent += (_, _) => sentEvents++;

        repeat.Start(payload, TimeSpan.FromMilliseconds(100));

        Assert.AreEqual(0, serialPort.SendCount);
        timer!.Fire();
        Assert.AreEqual(1, serialPort.SendCount);
        Assert.AreEqual(1, sentEvents);
    }

    [TestMethod]
    public void SendFailure_StopsRepeatingAndRaisesOneFailure()
    {
        var serialPort = new FakeSerialPortService { IsOpen = true, ShouldFailSend = true };
        using var connection = new ConnectionViewModel(serialPort);
        FakePeriodicTimer? timer = null;
        using var repeat = new RepeatSendViewModel(
            connection,
            callback => timer = new FakePeriodicTimer(callback));
        var failures = 0;
        repeat.SendFailed += () => failures++;

        repeat.Start(new PreparedSerialPayload([0x41], "A", false), TimeSpan.FromMilliseconds(100));
        timer!.Fire();
        timer.Fire();

        Assert.IsFalse(repeat.IsEnabled);
        Assert.AreEqual(1, failures);
    }

    private sealed class FakePeriodicTimer(Action callback) : IPeriodicTimer
    {
        public void Change(TimeSpan dueTime, TimeSpan period)
        {
        }

        public void Stop()
        {
        }

        public void Fire() => callback();

        public void Dispose()
        {
        }
    }

    private sealed class FakeSerialPortService : ISerialPortService
    {
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

        public string? PortName => IsOpen ? "COM1" : null;

        public bool ShouldFailSend { get; set; }

        public int SendCount { get; private set; }

        public IReadOnlyList<SerialPortInfoModel> GetAvailablePorts() => [];

        public void Open(SerialPortConnectionOptions options) => IsOpen = true;

        public void Close() => IsOpen = false;

        public void Send(byte[] data)
        {
            if (ShouldFailSend)
            {
                throw new IOException("Simulated failure.");
            }

            SendCount++;
        }

        public void Dispose()
        {
        }
    }
}
