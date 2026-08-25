using Comet.Core.Transmission;
using Comet.Services.Abstractions;

namespace Comet.ViewModels;

public sealed record RepeatedPayloadSentEventArgs(PreparedSerialPayload Payload, DateTime SentAt);

/// <summary>
/// Coordinates periodic writes independently of the UI dispatcher. Successful writes
/// and terminal presentation are reported separately so rendering cannot delay timing.
/// </summary>
public sealed class RepeatSendViewModel : IDisposable
{
    private readonly ConnectionViewModel _connection;
    private readonly IPeriodicTimer _timer;
    private PreparedSerialPayload? _payload;
    private int _isEnabled;
    private int _isSendInProgress;
    private int _failureRaised;

    public RepeatSendViewModel(
        ConnectionViewModel connection,
        Func<Action, IPeriodicTimer> timerFactory)
    {
        _connection = connection;
        _timer = timerFactory(OnTimerTick);
    }

    public event EventHandler<RepeatedPayloadSentEventArgs>? PayloadSent;

    public event Action? SendFailed;

    public bool IsEnabled => Volatile.Read(ref _isEnabled) != 0;

    public void Start(PreparedSerialPayload payload, TimeSpan interval)
    {
        // Using the interval as both due time and period preserves the contract that
        // the first write occurs after one complete interval, never immediately.
        Volatile.Write(ref _payload, payload);
        Volatile.Write(ref _isEnabled, 1);
        Volatile.Write(ref _failureRaised, 0);
        _timer.Change(interval, interval);
    }

    public void UpdateInterval(TimeSpan interval)
    {
        if (IsEnabled)
        {
            _timer.Change(interval, interval);
        }
    }

    public void UpdatePayload(PreparedSerialPayload? payload)
    {
        if (IsEnabled)
        {
            // Atomic replacement lets composer edits affect the next tick without
            // locking or mutating a payload currently being written.
            Volatile.Write(ref _payload, payload);
        }
    }

    public void Stop()
    {
        Volatile.Write(ref _isEnabled, 0);
        Volatile.Write(ref _payload, null);
        _timer.Stop();
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
            var payload = Volatile.Read(ref _payload);
            if (payload is null || !_connection.IsConnected)
            {
                RaiseFailure();
                return;
            }

            var sentAt = DateTime.Now;
            _connection.Send(payload.Bytes);
            // This event is raised on the timer thread. The WinUI view is responsible
            // for dispatching counter and terminal updates to its UI thread.
            PayloadSent?.Invoke(this, new RepeatedPayloadSentEventArgs(payload, sentAt));
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
        // until Start explicitly begins a new repeat-send session.
        if (Interlocked.Exchange(ref _failureRaised, 1) == 0)
        {
            SendFailed?.Invoke();
        }
    }

    public void Dispose() => _timer.Dispose();
}
