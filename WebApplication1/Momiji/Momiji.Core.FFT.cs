using Microsoft.Extensions.Logging;
using Momiji.Core.H264;
using Momiji.Interop;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.FFT
{
    public class FFTEncoder : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        private bool disposed = false;

        public FFTEncoder(
            ILoggerFactory loggerFactory
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<FFTEncoder>();

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
            //ISourceBlock<Wave.PcmBuffer<float>> inputQueue,
            //ITargetBlock<Wave.PcmBuffer<float>> inputReleaseQueue,
            ISourceBlock<H264InputBuffer> bufferQueue,
            ITargetBlock<H264InputBuffer> outputQueue,
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

                    //var pcm = inputQueue.Receive(ct);
                    var data = bufferQueue.Receive(ct);
                    data.Log.Clear();

                    //TODO FFT




                    //TODO H264Inputへの変換は別サービスにする

                    //inputReleaseQueue.Post(pcm);
                    outputQueue.Post(data);
                }
            }, ct);
        }
    }
}