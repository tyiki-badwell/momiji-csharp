using System;
using System.Diagnostics;
using System.Threading;

namespace Momiji.Core
{
    public class Timer : IDisposable
    {
        private bool disposed = false;

        private double startUsec;
        private Stopwatch stopwatch;

        public Timer()
        {
            startUsec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000.0;
            stopwatch = Stopwatch.StartNew();
        }

        public long USec {
            get {
                return (long)USecDouble;
            }
        }

        public double USecDouble
        {
            get
            {
                return startUsec + (((double)stopwatch.ElapsedTicks / Stopwatch.Frequency) * 1_000_000.0);
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
                if (stopwatch != null)
                {
                    stopwatch.Stop();
                    stopwatch = null;
                }
            }

            disposed = true;
        }
    }

    public class Waiter : IDisposable
    {
        private bool disposed = false;

        private Timer Timer { get; }
        private double Interval { get; }
        private CancellationToken Ct { get; }
        private double Before;
        private SemaphoreSlim S { get; }

        public Waiter(Timer timer, double interval, CancellationToken ct)
        {
            Timer = timer;
            Interval = interval;
            Ct = ct;
            Before = Timer.USecDouble;
            S = new SemaphoreSlim(1);
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
                S.Dispose();
            }

            disposed = true;
        }

        public void Wait()
        {
            var left = Interval - (Timer.USecDouble - Before);
            if (left > 0)
            {
                //セマフォで時間調整を行う
                S.Wait((int)(left / 1000), Ct);
            }
            else
            {
                //TODO マイナスだった場合はそのままスルー
            }
            Before = Timer.USecDouble;
        }
    }

}