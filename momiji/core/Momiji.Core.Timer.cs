using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Momiji.Core.Timer
{
    public class LapTimer : IDisposable
    {
        private bool disposed;

        private double StartUsec { get; }

        private Stopwatch stopwatch;

        public LapTimer()
        {
            StartUsec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000.0;
            stopwatch = Stopwatch.StartNew();
        }

        ~LapTimer()
        {
            Dispose(false);
        }

        public long USec
        {
            get
            {
                return (long)USecDouble;
            }
        }

        public double USecDouble
        {
            get
            {
                return StartUsec + (((double)stopwatch.ElapsedTicks / Stopwatch.Frequency) * 1_000_000.0);
            }
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

            stopwatch?.Stop();
            stopwatch = null;

            disposed = true;
        }
    }

    public class Waiter : IDisposable
    {
        private bool disposed;

        private LapTimer LapTimer { get; }
        private double Interval { get; }
        private double Before;

        public Waiter(LapTimer lapTimer, double interval)
        {
            LapTimer = lapTimer ?? throw new ArgumentNullException(nameof(lapTimer));
            Interval = interval;
            Before = LapTimer.USecDouble;
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

            disposed = true;
        }

        public async Task Wait(CancellationToken ct)
        {
            var left = Interval - (LapTimer.USecDouble - Before);
            if (left > 0)
            {
                //時間調整を行う
                await Task.Delay(new TimeSpan((long)(left * 10)), ct).ConfigureAwait(false);
            }
            else
            {
                //TODO マイナスだった場合はそのままスルー
            }
            Before = LapTimer.USecDouble;
        }
    }

}