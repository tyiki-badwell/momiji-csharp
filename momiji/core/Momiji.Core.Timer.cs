using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Momiji.Core
{
    public class Timer : IDisposable
    {
        private bool disposed;
        
        private double StartUsec { get; }

        private Stopwatch stopwatch;

        public Timer()
        {
            StartUsec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000.0;
            stopwatch = Stopwatch.StartNew();
        }

        ~Timer()
        {
            Dispose(false);
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

        private Timer Timer { get; }
        private double Interval { get; }
        private double Before;

        public Waiter(Timer timer, double interval)
        {
            Timer = timer ?? throw new ArgumentNullException(nameof(timer));
            Interval = interval;
            Before = Timer.USecDouble;
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
            var left = Interval - (Timer.USecDouble - Before);
            if (left > 0)
            {
                //時間調整を行う
                await Task.Delay(new TimeSpan((long)(left * 10)), ct).ConfigureAwait(false);
            }
            else
            {
                //TODO マイナスだった場合はそのままスルー
            }
            Before = Timer.USecDouble;
        }
    }

}