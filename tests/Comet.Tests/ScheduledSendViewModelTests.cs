using Comet.Core.Transmission;
using Comet.Models;
using Comet.Services.Abstractions;
using Comet.ViewModels;

namespace Comet.Tests;

[TestClass]
public sealed class ScheduledSendViewModelTests
{
    [TestMethod]
    public void StartRepeating_WaitsForFirstTickAndContinuesSending()
    {
        var serialPort = new FakeSerialPortService { IsOpen = true };
        using var connection = new ConnectionViewModel(serialPort);
        FakePeriodicTimer? timer = null;
        using var scheduler = new ScheduledSendViewModel(
            connection,
            callback => timer = new FakePeriodicTimer(callback));
        var payload = new PreparedSerialPayload([0x41], "A", false);
        var sentEvents = 0;
        scheduler.PayloadSent += (_, _) => sentEvents++;

        scheduler.StartRepeating(payload, TimeSpan.FromMilliseconds(100));

        Assert.AreEqual(0, serialPort.SentPayloads.Count);
        timer!.Fire();
        timer.Fire();
        Assert.AreEqual(ScheduledSendMode.RepeatPayload, scheduler.Mode);
        Assert.AreEqual(2, serialPort.SentPayloads.Count);
        Assert.AreEqual(2, sentEvents);
    }

    [TestMethod]
    public void StartPresetCycle_RepeatsSnapshotInOrderUntilStopped()
    {
        var serialPort = new FakeSerialPortService { IsOpen = true };
        using var connection = new ConnectionViewModel(serialPort);
        FakePeriodicTimer? timer = null;
        using var scheduler = new ScheduledSendViewModel(
            connection,
            callback => timer = new FakePeriodicTimer(callback));
        scheduler.StartPresetCycle(
            [
                new PreparedSerialPayload([0x41], "A", false),
                new PreparedSerialPayload([0x42], "B", false),
                new PreparedSerialPayload([0x43], "C", false)
            ],
            TimeSpan.FromMilliseconds(100));

        timer!.Fire();
        timer.Fire();
        timer.Fire();
        timer.Fire();
        timer.Fire();
        timer.Fire();
        scheduler.Stop();
        timer.Fire();

        CollectionAssert.AreEqual(
            new byte[] { 0x41, 0x42, 0x43, 0x41, 0x42, 0x43 },
            serialPort.SentPayloads.Select(payload => payload[0]).ToArray());
        Assert.AreEqual(ScheduledSendMode.None, scheduler.Mode);
    }

    [TestMethod]
    public void UpdateInterval_ChangesActivePresetCyclePeriod()
    {
        var serialPort = new FakeSerialPortService { IsOpen = true };
        using var connection = new ConnectionViewModel(serialPort);
        FakePeriodicTimer? timer = null;
        using var scheduler = new ScheduledSendViewModel(
            connection,
            callback => timer = new FakePeriodicTimer(callback));

        scheduler.StartPresetCycle(
            [new PreparedSerialPayload([0x41], "A", false)],
            TimeSpan.FromMilliseconds(100));
        scheduler.UpdateInterval(TimeSpan.FromMilliseconds(250));

        Assert.AreEqual(TimeSpan.FromMilliseconds(250), timer!.Period);
        Assert.AreEqual(2, timer.ChangeCount);
    }

    [TestMethod]
    public void Stop_DifferentModeKeepsActiveSchedule()
    {
        var serialPort = new FakeSerialPortService { IsOpen = true };
        using var connection = new ConnectionViewModel(serialPort);
        FakePeriodicTimer? timer = null;
        using var scheduler = new ScheduledSendViewModel(
            connection,
            callback => timer = new FakePeriodicTimer(callback));

        scheduler.StartRepeating(
            new PreparedSerialPayload([0x41], "A", false),
            TimeSpan.FromMilliseconds(100));
        scheduler.Stop(ScheduledSendMode.PresetCycle);
        timer!.Fire();

        Assert.AreEqual(ScheduledSendMode.RepeatPayload, scheduler.Mode);
        Assert.AreEqual(1, serialPort.SentPayloads.Count);
    }

    [TestMethod]
    public void SendFailure_StopsSchedulerAndRaisesOneFailure()
    {
        var serialPort = new FakeSerialPortService { IsOpen = true, ShouldFailSend = true };
        using var connection = new ConnectionViewModel(serialPort);
        FakePeriodicTimer? timer = null;
        using var scheduler = new ScheduledSendViewModel(
            connection,
            callback => timer = new FakePeriodicTimer(callback));
        var failures = 0;
        scheduler.SendFailed += () => failures++;

        scheduler.StartRepeating(
            new PreparedSerialPayload([0x41], "A", false),
            TimeSpan.FromMilliseconds(100));
        timer!.Fire();
        timer.Fire();

        Assert.IsFalse(scheduler.IsEnabled);
        Assert.AreEqual(1, failures);
    }

    private sealed class FakePeriodicTimer(Action callback) : IPeriodicTimer
    {
        public int ChangeCount { get; private set; }

        public TimeSpan Period { get; private set; }

        public void Change(TimeSpan dueTime, TimeSpan period)
        {
            ChangeCount++;
            Period = period;
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

        public bool IsConnectionActive => IsOpen;

        public string? PortName => IsOpen ? "COM1" : null;

        public bool ShouldFailSend { get; set; }

        public List<byte[]> SentPayloads { get; } = [];

        public IReadOnlyList<SerialPortInfoModel> GetAvailablePorts() => [];

        public void Open(SerialPortConnectionOptions options) => IsOpen = true;

        public void Close() => IsOpen = false;

        public void Send(byte[] data)
        {
            if (ShouldFailSend)
            {
                throw new IOException("Simulated failure.");
            }

            SentPayloads.Add(data.ToArray());
        }

        public void Dispose()
        {
        }
    }
}
