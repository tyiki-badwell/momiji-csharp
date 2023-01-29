using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Momiji.Interop.Kernel32;

namespace Momiji.Core.Timer;

public class TimerException : Exception
{
    public TimerException(string message) : base(message)
    {
    }

    public TimerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class ElapsedTimeCounter
{
    private long _startTimestamp;

    public ElapsedTimeCounter()
    {
        Reset();
    }

    public void Reset()
    {
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    public static readonly double TickFrequency = (double)10_000_000 / Stopwatch.Frequency;

    public static long TimestampToTicks(long timestamp)
    {
        return (long)(timestamp * TickFrequency);
    }

    public long Elapsed => Stopwatch.GetTimestamp() - _startTimestamp;

    public long ElapsedTicks => TimestampToTicks(Elapsed);

    public long NowTicks => TimestampToTicks(Stopwatch.GetTimestamp());
}

public class WaitableTimer : WaitHandle
{
    public WaitableTimer(
        bool manualReset,
        bool highResolution
    )
    {
        var handle =
            NativeMethods.CreateWaitableTimerExW(
                nint.Zero,
                default,
                (manualReset ? NativeMethods.WAITABLE_TIMER.FLAGS.MANUAL_RESET : 0)
                | (highResolution ? NativeMethods.WAITABLE_TIMER.FLAGS.HIGH_RESOLUTION : 0),
                NativeMethods.WAITABLE_TIMER.ACCESS_MASK.SYNCHRONIZE | NativeMethods.WAITABLE_TIMER.ACCESS_MASK.ALL_ACCESS
            );

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            throw new TimerException($"CreateWaitableTimerEx failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
        }

        SafeWaitHandle = handle;
    }

    public void Set(long dueTime)
    {
        var result =
            SafeWaitHandle.SetWaitableTimerEx(
                ref dueTime,
                0,
                nint.Zero,
                nint.Zero,
                nint.Zero,
                0
            );
        if (!result)
        {
            var error = Marshal.GetLastPInvokeError();
            throw new TimerException($"SetWaitableTimerEx failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
        }
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}


public class Waiter : IDisposable
{
    private bool _disposed;
    private readonly ElapsedTimeCounter _counter;
    private readonly long _intervalTicks;
    public long ProgressedFrames { private set; get; }

    private readonly WaitableTimer _timer;
    private long _progressedTicks = 0;

    public Waiter(ElapsedTimeCounter counter, long intervalTicks) : this(counter, intervalTicks, false)
    {
    }
    public Waiter(ElapsedTimeCounter counter, long intervalTicks, bool highResolution)
    {
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));
        if (intervalTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalTicks));
        }
        _intervalTicks = intervalTicks;

        _timer = new WaitableTimer(false, highResolution);

        _progressedTicks = _counter.ElapsedTicks;
        ProgressedFrames = 0;
    }

    public long Wait()
    {
        var nextTicks = _intervalTicks * (ProgressedFrames + 1);
        var leftTicks = nextTicks - _counter.ElapsedTicks;

        if (leftTicks > 0)
        {
            //１回分以内なら時間調整を行う
            var dueTime = leftTicks * -1;
            _timer.Set(dueTime);

            _timer.WaitOne();

            //待ち過ぎた時間を計測
            leftTicks = nextTicks - _counter.ElapsedTicks;
        }

        _progressedTicks = _counter.ElapsedTicks;
        ProgressedFrames = _progressedTicks / _intervalTicks;

        return leftTicks;
    }

    ~Waiter()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
        }
        _timer?.Dispose();

        _disposed = true;
    }
}
