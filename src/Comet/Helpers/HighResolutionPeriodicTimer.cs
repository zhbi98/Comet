using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Comet.Helpers;

/// <summary>
/// Runs periodic work from a Windows waitable timer without depending on the UI
/// dispatcher. The callback is invoked on a dedicated background thread.
/// </summary>
internal sealed class HighResolutionPeriodicTimer : IDisposable
{
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAccess = 0x00100002;
    private const uint WaitObject0 = 0;
    private const uint Infinite = 0xFFFFFFFF;

    private readonly Action _callback;
    private readonly nint _timerHandle;
    private readonly nint _controlEventHandle;
    private readonly nint[] _waitHandles;
    private readonly Thread _workerThread;
    private volatile bool _isRunning;
    private volatile bool _isDisposed;

    public HighResolutionPeriodicTimer(Action callback)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        // High-resolution timers are unavailable on some supported Windows versions;
        // the ordinary waitable timer preserves behavior on those systems.
        var timerHandle = CreateWaitableTimerEx(
            0,
            null,
            CreateWaitableTimerHighResolution,
            TimerAccess);
        if (timerHandle == 0)
        {
            timerHandle = CreateWaitableTimerEx(0, null, 0, TimerAccess);
        }

        if (timerHandle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _timerHandle = timerHandle;

        _controlEventHandle = CreateEvent(0, false, false, null);
        if (_controlEventHandle == 0)
        {
            var error = Marshal.GetLastWin32Error();
            _ = CloseHandle(_timerHandle);
            throw new Win32Exception(error);
        }

        _waitHandles = [_timerHandle, _controlEventHandle];
        _workerThread = new Thread(WaitForTicks)
        {
            IsBackground = true,
            Name = "Comet repeat send timer"
        };
        _workerThread.Start();
    }

    public void Change(TimeSpan dueTime, TimeSpan period)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        // Win32 represents a relative due time as a negative count of 100 ns units.
        var dueTimeTicks = -Math.Max(1, (long)Math.Round(dueTime.TotalMilliseconds * 10_000d));
        var periodMilliseconds = checked((int)Math.Round(period.TotalMilliseconds));
        if (periodMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period));
        }

        _isRunning = true;
        if (!SetWaitableTimer(
                _timerHandle,
                ref dueTimeTicks,
                periodMilliseconds,
                0,
                0,
                false))
        {
            _isRunning = false;
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Stop()
    {
        if (_isDisposed)
        {
            return;
        }

        _isRunning = false;
        _ = CancelWaitableTimer(_timerHandle);
        _ = SetEvent(_controlEventHandle);
    }

    private void WaitForTicks()
    {
        while (!_isDisposed)
        {
            // The control event interrupts an infinite timer wait when Stop or Dispose
            // changes the timer state.
            var waitResult = WaitForMultipleObjects(
                (uint)_waitHandles.Length,
                _waitHandles,
                false,
                Infinite);
            if (_isDisposed)
            {
                return;
            }

            if (waitResult == WaitObject0 && _isRunning)
            {
                _callback();
            }
            else if (waitResult != WaitObject0 + 1)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isRunning = false;
        _isDisposed = true;
        _ = CancelWaitableTimer(_timerHandle);
        _ = SetEvent(_controlEventHandle);
        // Handles remain valid until the worker has observed the dispose signal.
        _workerThread.Join();
        _ = CloseHandle(_controlEventHandle);
        _ = CloseHandle(_timerHandle);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
    private static extern nint CreateWaitableTimerEx(
        nint timerAttributes,
        string? timerName,
        uint flags,
        uint desiredAccess);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateEventW", SetLastError = true)]
    private static extern nint CreateEvent(
        nint eventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(
        nint timerHandle,
        ref long dueTime,
        int period,
        nint completionRoutine,
        nint completionRoutineArgument,
        [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelWaitableTimer(nint timerHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForMultipleObjects(
        uint handleCount,
        nint[] handles,
        [MarshalAs(UnmanagedType.Bool)] bool waitAll,
        uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(nint eventHandle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
