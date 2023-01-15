using System.Diagnostics;
using System.Runtime.InteropServices;
using Momiji.Interop.Kernel32;

namespace Momiji.Core.Timer;

public class TimerException : Exception
{
    public TimerException()
    {
    }

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

public class Waiter : IDisposable
{
    private bool _disposed;
    private readonly ElapsedTimeCounter _counter;
    private readonly long _intervalTicks;
    public long ProgressedFrames { private set; get; }

    private readonly WaitableTimer _handle;
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
        _progressedTicks = _counter.ElapsedTicks;

        _handle =
            NativeMethods.CreateWaitableTimerEx(
                IntPtr.Zero,
                IntPtr.Zero,
                WaitableTimer.FLAGS.MANUAL_RESET | (highResolution ? WaitableTimer.FLAGS.HIGH_RESOLUTION : 0),
                WaitableTimer.ACCESSES.SYNCHRONIZE | WaitableTimer.ACCESSES.TIMER_ALL_ACCESS
            );

        if (_handle.IsInvalid)
        {
            throw new TimerException($"CreateWaitableTimerEx failed [{Marshal.GetLastWin32Error()}]");
        }

        Reset();
    }

    private void Reset()
    {
        _progressedTicks = _counter.ElapsedTicks;
        ProgressedFrames = _progressedTicks / _intervalTicks;
    }

    public int Wait()
    {
        _progressedTicks += _intervalTicks;
        var nowTicks = _counter.ElapsedTicks;
        var left = _progressedTicks - nowTicks;

        var frames = 1;

        if (left > 0)
        {
            //１回分以内なら時間調整を行う
            {
                var dueTime = left * -1;
                var result =
                    _handle.SetWaitableTimerEx(
                        ref dueTime,
                        0,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        0
                    );
                if (!result)
                {
                    throw new TimerException($"SetWaitableTimerEx failed [{Marshal.GetLastWin32Error()}]");
                }
            }
            {
                var result = _handle.WaitForSingleObject(0xFFFFFFFF);
                if (result != 0)
                {
                    throw new TimerException($"WaitForSingleObject failed [{Marshal.GetLastWin32Error()}]");
                }
            }
        }
        else
        {
            //遅れているならバーストさせる
            frames = (int)(left / _intervalTicks * -1)+1;
            _progressedTicks = nowTicks;
        }

        ProgressedFrames = _progressedTicks / _intervalTicks;

        return frames;
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

        if (_handle != default)
        {
            if (
                !_handle.IsInvalid
                && !_handle.IsClosed
            )
            {
                _handle.Close();
            }
        }

        _disposed = true;
    }
}
