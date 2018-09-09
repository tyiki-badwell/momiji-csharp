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

        private bool disposed = false;

        public ToPcm(
            ILoggerFactory loggerFactory,
            Timer timer
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<ToPcm<T>>();
            Timer = timer;
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
            dest.Log.Marge(source.Log);

            var target = dest.Target;
            var targetIdx = 0;
            var left = source[0];
            var right = source[1];

            dest.Log.Add("[to pcm] start", Timer.USecDouble);
            for (var idx = 0; idx < left.Length; idx++)
            {
                target[targetIdx++] = left[idx];
                target[targetIdx++] = right[idx];
            }
            dest.Log.Add("[to pcm] end", Timer.USecDouble);
        }

        public async Task Run(
            ISourceBlock<VstBuffer<T>> sourceQueue,
            ITargetBlock<VstBuffer<T>> sourceReleaseQueue,
            ISourceBlock<PcmBuffer<T>> destQueue,
            ITargetBlock<PcmBuffer<T>> destReleaseQueue,
            CancellationToken ct)
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    var source = sourceQueue.Receive(ct);
                    source.Log.Add("[to pcm] source get", Timer.USecDouble);
                    var dest = destQueue.Receive(ct);
                    source.Log.Add("[to pcm] dest get", Timer.USecDouble);
                    Execute(source, dest);
                    sourceReleaseQueue.Post(source);
                    destReleaseQueue.Post(dest);
                }
            }, ct);
        }
    }
}