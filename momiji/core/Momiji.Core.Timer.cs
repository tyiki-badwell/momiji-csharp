﻿using System;
using System.Diagnostics;
using System.Threading;

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
        private CancellationToken Ct { get; }
        private double Before;
        private SemaphoreSlim S { get; set; }

        public Waiter(Timer timer, double interval, CancellationToken ct)
        {
            Timer = timer ?? throw new ArgumentNullException(nameof(timer));
            Interval = interval;
            Ct = ct;
            Before = Timer.USecDouble;
            S = new SemaphoreSlim(1);
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

            S?.Dispose();
            S = null;

            disposed = true;
        }

        public void Wait()
        {
            var left = Interval - (Timer.USecDouble - Before);
            if (left > 0)
            {
                //セマフォで時間調整を行う
                S.Wait(new TimeSpan((long)(left * 10)), Ct);
            }
            else
            {
                //TODO マイナスだった場合はそのままスルー
            }
            Before = Timer.USecDouble;
        }
    }

}