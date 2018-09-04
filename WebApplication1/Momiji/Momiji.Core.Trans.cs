using Microsoft.Extensions.Logging;
using Momiji.Core.Vst;
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

        public async Task Run(
            ISourceBlock<VstBuffer<T>> inputQueue,
            ITargetBlock<VstBuffer<T>> inputReleaseQueue,
            ISourceBlock<Wave.PcmBuffer<T>> bufferQueue,
            ITargetBlock<Wave.PcmBuffer<T>> outputQueue,
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

                    var buffer = inputQueue.Receive(ct);
                    var data = bufferQueue.Receive(ct);
                    data.Log.Marge(buffer.Log);

                    var target = data.Target;
                    var targetIdx = 0;
                    var left = buffer.Get(0);
                    var right = buffer.Get(1);

                    data.Log.Add("[to pcm] start", Timer.USecDouble);
                    for (var idx = 0; idx < left.Length; idx++)
                    {
                        target[targetIdx++] = left[idx];
                        target[targetIdx++] = right[idx];
                    }
                    data.Log.Add("[to pcm] end", Timer.USecDouble);

                    inputReleaseQueue.Post(buffer);
                    outputQueue.Post(data);
                }
            }, ct);
        }
    }
}