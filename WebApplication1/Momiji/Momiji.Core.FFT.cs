using Microsoft.Extensions.Logging;
using Momiji.Core.H264;
using Momiji.Core.Wave;
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
            PcmBuffer<float> source,
            H264InputBuffer dest
        )
        {
            dest.Log.Marge(source.Log);
            dest.Log.Add("[fft] start", Timer.USecDouble);
            unsafe
            {
                var target = dest.Target;
                {
                    var length = target.iPicWidth * target.iPicHeight;
                    var y = new Span<byte>((byte*)target.pData0, length);
                    var value = a;
                    a++;
                    if (a > 235)
                    {
                        a = 16;
                    }
                    for (var idx = 0; idx < length; idx++)
                    {
                        y[idx] = (byte)value++;
                        if (value > 235)
                        {
                            value = 16;
                        }
                    }
                }

                {
                    var length = target.iPicWidth * target.iPicHeight >> 2;
                    var u = new Span<byte>((byte*)target.pData1, length);
                    var value = a;
                    for (var idx = 0; idx < length; idx++)
                    {
                        u[idx] = (byte)value++;
                        if (value > 235)
                        {
                            value = 16;
                        }
                    }
                }

                {
                    var length = target.iPicWidth * target.iPicHeight >> 2;
                    var v = new Span<byte>((byte*)target.pData2, length); 
                    var value = a;
                    for (var idx = 0; idx < length; idx++)
                    {
                        v[idx] = (byte)value++;
                        if (value > 235)
                        {
                            value = 16;
                        }
                    }
                }
            }
            dest.Log.Add("[fft] end", Timer.USecDouble);
        }
    }
}