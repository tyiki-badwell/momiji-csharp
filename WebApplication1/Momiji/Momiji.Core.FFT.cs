using Microsoft.Extensions.Logging;
using Momiji.Core.H264;
using Momiji.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.FFT
{
    public class FFTEncoder : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private int PicWidth { get; }
        private int PicHeight { get; }
        private float MaxFrameRate { get; }

        private bool disposed = false;

        public FFTEncoder(
            int picWidth,
            int picHeight,
            float maxFrameRate,
            ILoggerFactory loggerFactory,
            Timer timer
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<FFTEncoder>();
            Timer = timer;

            PicWidth = picWidth;
            PicHeight = picHeight;
            MaxFrameRate = maxFrameRate;

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

        private static int a = 16;
        public void Execute(
            H264InputBuffer dest
        )
        {
            var target = dest.Target;
            {
                var y = target.pData0;
                var length = target.iPicWidth * target.iPicHeight;

                var value = a;
                a++;
                if (a > 235)
                {
                    a = 16;
                }
                for (var idx = 0; idx < length; idx++)
                {
                    Marshal.WriteByte(y, (byte)value++);
                    y += 1;
                    if (value > 235)
                    {
                        value = 16;
                    }
                }
            }

            {
                var u = target.pData1;
                var length = target.iPicWidth * target.iPicHeight >> 2;
                var value = a;
                for (var idx = 0; idx < length; idx++)
                {
                    Marshal.WriteByte(u, (byte)value++);
                    u += 1;
                    if (value > 235)
                    {
                        value = 16;
                    }
                }
            }

            {
                var v = target.pData2;
                var length = target.iPicWidth * target.iPicHeight >> 2;
                var value = a;
                for (var idx = 0; idx < length; idx++)
                {
                    Marshal.WriteByte(v, (byte)value++);
                    v += 1;
                    if (value > 235)
                    {
                        value = 16;
                    }
                }
            }

        }

        public async Task Run(
            //ISourceBlock<Wave.PcmBuffer<float>> inputQueue,
            //ITargetBlock<Wave.PcmBuffer<float>> inputReleaseQueue,
            ISourceBlock<H264InputBuffer> destQueue,
            ITargetBlock<H264InputBuffer> destReleaseQueue,
            CancellationToken ct)
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var interval = 1000000.0 / MaxFrameRate;
                var before = Timer.USecDouble;
                using (var w = new Waiter(Timer, interval, ct))
                {
                    while (true)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            break;
                        }

                        //var pcm = inputQueue.Receive(ct);
                        var dest = destQueue.Receive(ct);
                        dest.Log.Clear();

                        w.Wait();
                        Execute(dest);
                        //TODO H264Inputへの変換は別サービスにする

                        //inputReleaseQueue.Post(pcm);
                        destReleaseQueue.Post(dest);
                    }
                }
            }, ct);
        }
    }
}