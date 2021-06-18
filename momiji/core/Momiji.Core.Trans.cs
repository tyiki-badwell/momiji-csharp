using Microsoft.Extensions.Logging;
using Momiji.Core.Timer;
using Momiji.Core.Vst;
using Momiji.Core.Wave;
using System;

namespace Momiji.Core.Trans
{
    public class ToPcm<T> : IDisposable where T : struct
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private LapTimer LapTimer { get; }

        private bool disposed;

        public ToPcm(
            ILoggerFactory loggerFactory,
            LapTimer lapTimer
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<ToPcm<T>>();
            LapTimer = lapTimer;
        }

        ~ToPcm()
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

        public void Execute(
            VstBuffer<T> source,
            PcmBuffer<T> dest
        )
        {
            if (source == default)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (dest == default)
            {
                throw new ArgumentNullException(nameof(dest));
            }

            dest.Log.Marge(source.Log);

            var targetIdx = 0;
            var target = new Span<T>(dest.Buffer.Target);
            var left = new ReadOnlySpan<T>(source.GetChannelBuffer(0));
            var right = new ReadOnlySpan<T>(source.GetChannelBuffer(1));

            dest.Log.Add("[to pcm] start", LapTimer.USecDouble);
            for (var idx = 0; idx < left.Length; idx++)
            {
                target[targetIdx++] = left[idx];
                target[targetIdx++] = right[idx];
            }
            dest.Log.Add("[to pcm] end", LapTimer.USecDouble);
        }
        public void Execute(
            VstBuffer2<T> source,
            PcmBuffer<T> dest
        )
        {
            if (source == default)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (dest == default)
            {
                throw new ArgumentNullException(nameof(dest));
            }

            dest.Log.Marge(source.Log);

            var targetIdx = 0;
            var target = new Span<T>(dest.Buffer.Target);
            unsafe
            {
                var left = new ReadOnlySpan<T>(source.GetChannelBuffer(0).ToPointer(), source.BlockSize);
                var right = new ReadOnlySpan<T>(source.GetChannelBuffer(1).ToPointer(), source.BlockSize);

                dest.Log.Add("[to pcm] start", LapTimer.USecDouble);
                for (var idx = 0; idx < left.Length; idx++)
                {
                    target[targetIdx++] = left[idx];
                    target[targetIdx++] = right[idx];
                }
            }
            dest.Log.Add("[to pcm] end", LapTimer.USecDouble);
        }
    }
}