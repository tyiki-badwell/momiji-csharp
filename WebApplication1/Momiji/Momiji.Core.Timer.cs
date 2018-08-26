using System;
using System.Diagnostics;

namespace Momiji.Core
{
    public class Timer : IDisposable
    {
        private bool disposed = false;

        private double startUsec;
        private Stopwatch stopwatch;

        public Timer()
        {
            startUsec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
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
                return startUsec + ((double)stopwatch.ElapsedTicks / Stopwatch.Frequency * 1000000);
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
}