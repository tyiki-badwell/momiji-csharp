using Momiji.Interop;
using System;
using System.Diagnostics;
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
        private bool disposed = false;
        private Interop.Opus.OpusEncoder encoder;

        public OpusEncoder(
            Interop.Opus.SamplingRate Fs,
            Interop.Opus.Channels channels
        )
        {
            var error = Interop.Opus.OpusStatusCode.Unimplemented;

            encoder =
                Interop.Opus.opus_encoder_create(
                    Fs, channels, Interop.Opus.OpusApplicationType.Audio, out error
                );

            if (error != Interop.Opus.OpusStatusCode.OK)
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
                encoder.Close();
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

                Wave.PcmBuffer<float> pcm = null;

                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        if (pcm == null)
                        {
                            pcm = inputQueue.Receive(new TimeSpan(20_000_000), ct);
                            //Trace.WriteLine("[opus] receive pcm");
                        }
                        var data = bufferQueue.Receive(new TimeSpan(20_000_000), ct);
                        //Trace.WriteLine("[opus] get data");

                        data.Wrote = Interop.Opus.opus_encode_float(
                            encoder,
                            pcm.AddrOfPinnedObject(),
                            pcm.Target.Length / 2,
                            data.AddrOfPinnedObject(),
                            data.Target.Length
                            );

                        inputReleaseQueue.Post(pcm);
                        pcm = null;
                        //Trace.WriteLine("[opus] release pcm");
                        if (data.Wrote < 0)
                        {
                            throw new Exception($"[opus] opus_encode_float error:{data.Wrote}");
                        }
                        else
                        {
                            //Trace.WriteLine($"[opus] post data: wrote {data.Wrote}");
                        }

                        outputQueue.Post(data);
                    }
                    catch (TimeoutException te)
                    {
                        Trace.WriteLine("[opus] timeout");
                        continue;
                    }
                }
            }, ct);
        }
    }
}