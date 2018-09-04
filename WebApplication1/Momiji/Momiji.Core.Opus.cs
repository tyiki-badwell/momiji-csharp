using Microsoft.Extensions.Logging;
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
            ISourceBlock<Wave.PcmBuffer<float>> inputQueue,
            ITargetBlock<Wave.PcmBuffer<float>> inputReleaseQueue,
            ISourceBlock<OpusOutputBuffer> bufferQueue,
            ITargetBlock<OpusOutputBuffer> outputQueue,
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

                    var pcm = inputQueue.Receive(ct);
                    var data = bufferQueue.Receive(ct);
                    data.Log.Marge(pcm.Log);

                    data.Log.Add("[opus] start opus_encode_float", Timer.USecDouble);
                    data.Wrote = encoder.opus_encode_float(
                        pcm.AddrOfPinnedObject,
                        pcm.Target.Length / 2,
                        data.AddrOfPinnedObject,
                        data.Target.Length
                        );
                    data.Log.Add($"[opus] end opus_encode_float {data.Wrote}", Timer.USecDouble);

                    inputReleaseQueue.Post(pcm);
                    if (data.Wrote < 0)
                    {
                        throw new Exception($"[opus] opus_encode_float error:{data.Wrote}");
                    }

                    outputQueue.Post(data);
                }
            }, ct);
        }
    }
}