using Microsoft.Extensions.Logging;
using Momiji.Core.Timer;
using Momiji.Core.Wave;
using Momiji.Interop.Buffer;
using Momiji.Interop.Opus;
using System;

namespace Momiji.Core.Opus
{
    public class OpusException : Exception
    {
        public OpusException()
        {
        }

        public OpusException(string message) : base(message)
        {
        }

        public OpusException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public class OpusOutputBuffer : IDisposable
    {
        private bool disposed;
        internal PinnedBuffer<byte[]> Buffer { get; }
        public BufferLog Log { get; }
        public int Wrote { get; set; }

        public OpusOutputBuffer(int size)
        {
            Buffer = new(new byte[size]);
            Log = new();
        }
        ~OpusOutputBuffer()
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

            Buffer?.Dispose();
            disposed = true;
        }
    }

    public class OpusEncoder : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private LapTimer LapTimer { get; }

        private bool disposed;
        private Encoder encoder;

        public OpusEncoder(
            SamplingRate Fs,
            Channels channels,
            ILoggerFactory loggerFactory,
            LapTimer lapTimer
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<OpusEncoder>();
            LapTimer = lapTimer;

            Logger.LogInformation($"opus version {SafeNativeMethods.opus_get_version_string()}");

            encoder =
                SafeNativeMethods.opus_encoder_create(
                    Fs, channels, OpusApplicationType.Audio, out var error
                );

            if (error != OpusStatusCode.OK)
            {
                throw new OpusException($"[opus] opus_encoder_create error:{SafeNativeMethods.opus_strerror((int)error)}({error})");
            }
        }

        ~OpusEncoder()
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

            if (encoder != null)
            {
                if (
                    !encoder.IsInvalid
                    && !encoder.IsClosed
                )
                {
                    encoder.Close();
                }
                encoder = null;
            }

            disposed = true;
        }

        public void Execute(
            PcmBuffer<float> source,
            OpusOutputBuffer dest
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

            dest.Log.Add("[opus] start opus_encode_float", LapTimer.USecDouble);
            dest.Wrote = encoder.opus_encode_float(
                source.Buffer.AddrOfPinnedObject,
                source.Buffer.Target.Length / 2,
                dest.Buffer.AddrOfPinnedObject,
                dest.Buffer.Target.Length
                );
            /*
             この式を満たさないとダメ
             TODO 満たすように分結する仕組み要る？？？
              if (blockSize<samplingRate/400)
                return -1;
              if (400*blockSize!=samplingRate   && 200*blockSize!=samplingRate   && 100*blockSize!=samplingRate   &&
                  50*blockSize!=samplingRate   &&  25*blockSize!=samplingRate   &&  50*blockSize!=3*samplingRate &&
                  50*blockSize!=4*samplingRate &&  50*blockSize!=5*samplingRate &&  50*blockSize!=6*samplingRate)
                return -1;
            */
            dest.Log.Add($"[opus] end opus_encode_float {dest.Wrote}", LapTimer.USecDouble);
            if (dest.Wrote < 0)
            {
                throw new OpusException($"[opus] opus_encode_float error:{SafeNativeMethods.opus_strerror(dest.Wrote)}({dest.Wrote})");
            }
        }
    }
}
