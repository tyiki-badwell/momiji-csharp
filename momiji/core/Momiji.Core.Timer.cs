using Momiji.Interop.Kernel32;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Momiji.Core.Timer
{
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
        private long StartTicks { get; set; }

        private long StartTimestamp { get; set; }

        public ElapsedTimeCounter()
        {
            Reset();
        }

        public void Reset()
        {
            StartTicks = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 10_000;
            StartTimestamp = Stopwatch.GetTimestamp();
        }

        public long Elapsed
        {
            get
            {
                return Stopwatch.GetTimestamp() - StartTimestamp;
            }
        }

        public long ElapsedTicks
        {
            get
            {
                return (Elapsed * 10_000_000) / Stopwatch.Frequency;
            }
        }

        public long NowTicks
        {
            get
            {
                return StartTicks + ElapsedTicks;
            }
        }
    }

    public class Waiter : IDisposable
    {
        private bool disposed;
        private ElapsedTimeCounter Counter { get; }
        private long IntervalTicks { get; }

        public long BeforeFlames { set; get; }

        private WaitableTimer handle;

        public Waiter(ElapsedTimeCounter counter, long intervalTicks) : this(counter, intervalTicks, false)
        {
        }
        public Waiter(ElapsedTimeCounter counter, long intervalTicks, bool highResolution)
        {
            Counter = counter ?? throw new ArgumentNullException(nameof(counter));
            if (intervalTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(intervalTicks));
            }
            IntervalTicks = intervalTicks;

            handle =
                NativeMethods.CreateWaitableTimerEx(
                    IntPtr.Zero,
                    IntPtr.Zero,
                    WaitableTimer.FLAGS.MANUAL_RESET | (highResolution ? WaitableTimer.FLAGS.HIGH_RESOLUTION : 0),
                    WaitableTimer.ACCESSES.SYNCHRONIZE | WaitableTimer.ACCESSES.TIMER_ALL_ACCESS
                );

            if (handle.IsInvalid)
            {
                throw new TimerException($"CreateWaitableTimerEx failed [{Marshal.GetLastWin32Error()}]");
            }

            Reset();
        }

        private void Reset()
        {
            BeforeFlames = Counter.ElapsedTicks / IntervalTicks;
        }

        public int Wait()
        {
            var left = (IntervalTicks * (BeforeFlames+1)) - Counter.ElapsedTicks;
            var frames = 1;

            if (left > 0)
            {
                //１回分以内なら時間調整を行う
                {
                    var dueTime = left * -1;
                    var result =
                        handle.SetWaitableTimerEx(
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
                    var result = handle.WaitForSingleObject(0xFFFFFFFF);
                    if (result != 0)
                    {
                        throw new TimerException($"WaitForSingleObject failed [{Marshal.GetLastWin32Error()}]");
                    }
                }
            }
            else
            {
                //遅れているならバーストさせる
                frames = (int)(left / IntervalTicks * -1)+1;
            }

            Reset();

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
            if (disposed) return;

            if (disposing)
            {
            }

            if (handle != default)
            {
                if (
                    !handle.IsInvalid
                    && !handle.IsClosed
                )
                {
                    handle.Close();
                }
                handle = default;
            }

            disposed = true;
        }
    }

}