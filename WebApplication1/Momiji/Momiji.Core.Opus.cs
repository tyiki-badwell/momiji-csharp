using Microsoft.Extensions.Logging;
using Momiji.Core.Wave;
using Momiji.Interop;
using Momiji.Interop.Opus;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.Opus
{
    public class OpusOutputBuffer : PinnedBuffer<byte[]>
    {
        public int Wrote { get; set; }

        public OpusOutputBuffer(int size) : base(new byte[size])
        {
        }
    }

    public class OpusEncoder : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private bool disposed = false;
        private Encoder encoder;

        public OpusEncoder(
            SamplingRate Fs,
            Channels channels, 
            ILoggerFactory loggerFactory,
            Timer timer
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<OpusEncoder>();
            Timer = timer;

            var error = OpusStatusCode.Unimplemented;

            encoder =
                Encoder.opus_encoder_create(
                    Fs, channels, OpusApplicationType.Audio, out error
                );

            if (error != OpusStatusCode.OK)
            {
                throw new Exception($"[opus] opus_encoder_create error:{error}");
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
                if (encoder != null && !encoder.IsInvalid)
                {
                    encoder.Close();
                    encoder = null;
                }
            }

            disposed = true;
        }

        public async Task Run(
            ISourceBlock<PcmBuffer<float>> sourceQueue,
            ITargetBlock<PcmBuffer<float>> sourceReleaseQueue,
            ISourceBlock<OpusOutputBuffer> destQueue,
            ITargetBlock<OpusOutputBuffer> destReleaseQueue,
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
                    var dest = destQueue.Receive(ct);

                    Execute(source, dest);

                    sourceReleaseQueue.Post(source);
                    destReleaseQueue.Post(dest);
                }
            }, ct);
        }

        public void Execute(
            PcmBuffer<float> source,
            OpusOutputBuffer dest
        )
        {
            dest.Log.Marge(source.Log);

            dest.Log.Add("[opus] start opus_encode_float", Timer.USecDouble);
            dest.Wrote = encoder.opus_encode_float(
                source.AddrOfPinnedObject,
                source.Target.Length / 2,
                dest.AddrOfPinnedObject,
                dest.Target.Length
                );
            dest.Log.Add($"[opus] end opus_encode_float {dest.Wrote}", Timer.USecDouble);
            if (dest.Wrote < 0)
            {
                throw new Exception($"[opus] opus_encode_float error:{dest.Wrote}");
            }
        }
    }
}