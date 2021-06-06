using Microsoft.Extensions.Logging;
using Momiji.Core.Vst;
using Momiji.Core.Wave;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.Trans
{
    public class ToPcm<T> : IDisposable where T : struct
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private bool disposed;

        public ToPcm(
            ILoggerFactory loggerFactory,
            Timer timer
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<ToPcm<T>>();
            Timer = timer;
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
            var target = new Span<T>(dest.Target);
            var left = new ReadOnlySpan<T>(source.GetChannelBuffer(0));
            var right = new ReadOnlySpan<T>(source.GetChannelBuffer(1));

            dest.Log.Add("[to pcm] start", Timer.USecDouble);
            for (var idx = 0; idx < left.Length; idx++)
            {
                target[targetIdx++] = left[idx];
                target[targetIdx++] = right[idx];
            }
            dest.Log.Add("[to pcm] end", Timer.USecDouble);
        }
    }
}