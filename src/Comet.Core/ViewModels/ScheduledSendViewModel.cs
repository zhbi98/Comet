using Comet.Core.Transmission;
using Comet.Services.Abstractions;

namespace Comet.ViewModels;

public enum ScheduledSendMode
{
    None,
    RepeatPayload,
    PresetCycle
}

public sealed record ScheduledPayloadSentEventArgs(PreparedSerialPayload Payload, DateTime SentAt);

/// <summary>
/// Coordinates repeated single-payload and cyclic-sequence writes independently of
/// the UI dispatcher.
/// Successful writes are reported separately so terminal rendering cannot delay timing.
/// </summary>
public sealed class ScheduledSendViewModel : IDisposable
{
    private readonly ConnectionViewModel _connection;
    private readonly IPeriodicTimer _timer;
    private PreparedSerialPayload[] _payloads = [];
    private int _failureRaised;
    private int _isSendInProgress;
    private int _mode;
    private int _nextPayloadIndex;

    public ScheduledSendViewModel(
        ConnectionViewModel connection,
        Func<Action, IPeriodicTimer> timerFactory)
    {
        _connection = connection;
        _timer = timerFactory(OnTimerTick);
    }

    public event EventHandler<ScheduledPayloadSentEventArgs>? PayloadSent;

    public event Action? SendFailed;

    public ScheduledSendMode Mode => (ScheduledSendMode)Volatile.Read(ref _mode);

    public bool IsEnabled => Mode != ScheduledSendMode.None;

    public void StartRepeating(PreparedSerialPayload payload, TimeSpan interval) =>
        StartCore([payload], interval, ScheduledSendMode.RepeatPayload);

    public void StartPresetCycle(IReadOnlyList<PreparedSerialPayload> payloads, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        if (payloads.Count == 0)
        {
            throw new ArgumentException("A preset cycle requires at least one payload.", nameof(payloads));
        }

        // Copy the presets so UI edits cannot mutate a cycle already running on
        // the timer thread. Each cycle sends exactly this many payloads.
        StartCore(payloads.ToArray(), interval, ScheduledSendMode.PresetCycle);
    }

    public void UpdateInterval(TimeSpan interval)
    {
        if (IsEnabled)
        {
            _timer.Change(interval, interval);
        }
    }

    public void UpdateRepeatingPayload(PreparedSerialPayload? payload)
    {
        if (Mode == ScheduledSendMode.RepeatPayload)
        {
            // Atomic replacement lets composer edits affect the next tick without
            // mutating a payload currently being written.
            Volatile.Write(ref _payloads, payload is null ? [] : [payload]);
        }
    }

    public void Stop(ScheduledSendMode mode)
    {
        if (Mode == mode)
        {
            Stop();
        }
    }

    public void Stop()
    {
        Volatile.Write(ref _mode, (int)ScheduledSendMode.None);
        Volatile.Write(ref _payloads, []);
        Volatile.Write(ref _nextPayloadIndex, 0);
        _timer.Stop();
    }

    private void StartCore(
        PreparedSerialPayload[] payloads,
        TimeSpan interval,
        ScheduledSendMode mode)
    {
        Volatile.Write(ref _payloads, payloads);
        Volatile.Write(ref _nextPayloadIndex, 0);
        Volatile.Write(ref _failureRaised, 0);
        Volatile.Write(ref _mode, (int)mode);

        // The first write occurs after one complete interval, matching the existing
        // bottom repeat-send timing contract.
        _timer.Change(interval, interval);
    }

    private void OnTimerTick()
    {
        // A slow serial write must not overlap the next periodic callback.
        if (!IsEnabled || Interlocked.Exchange(ref _isSendInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            var mode = Mode;
            var payloads = Volatile.Read(ref _payloads);
            if (payloads.Length == 0 || !_connection.IsConnected)
            {
                RaiseFailure();
                return;
            }

            var payloadIndex = mode == ScheduledSendMode.PresetCycle
                ? Volatile.Read(ref _nextPayloadIndex)
                : 0;
            var payload = payloads[payloadIndex];
            var sentAt = DateTime.Now;
            _connection.Send(payload.Bytes);
            // This event is raised on the timer thread. The WinUI view dispatches
            // counter and terminal updates without blocking the scheduler.
            PayloadSent?.Invoke(this, new ScheduledPayloadSentEventArgs(payload, sentAt));

            if (mode == ScheduledSendMode.PresetCycle)
            {
                // Wrap after the last snapshot item and continue until Stop is called.
                Volatile.Write(ref _nextPayloadIndex, (payloadIndex + 1) % payloads.Length);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            RaiseFailure();
        }
        finally
        {
            Volatile.Write(ref _isSendInProgress, 0);
        }
    }

    private void RaiseFailure()
    {
        Stop();
        // Several callbacks can observe the same disconnect. Publish one UI failure
        // until a new scheduled-send session starts.
        if (Interlocked.Exchange(ref _failureRaised, 1) == 0)
        {
            SendFailed?.Invoke();
        }
    }

    public void Dispose() => _timer.Dispose();
}
