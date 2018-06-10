﻿using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading.Tasks;
using Momiji.Interop;
using System.Threading;
using System.Security.Permissions;
using System.Threading.Tasks.Dataflow;

namespace Momiji
{
    namespace Core
    {
        public class Opus
        {
            public class OpusOutputBuffer : PinnedBuffer<byte[]>
            {
                public int Wrote { get; set; }

                public OpusOutputBuffer(int size): base(new byte[size])
                {
                }
            }

            public class OpusEncoder : IDisposable
            {
                private bool disposed = false;
                private Interop.Opus.OpusEncoder encoder;
                private CancellationTokenSource processCancel = new CancellationTokenSource();
                private Task processTask;

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
                        throw new Exception($"opus_encoder_create error:{error}");
                    }
                }

                public void Run(
                    ISourceBlock<PinnedBuffer<float[]>> inputQueue,
                    ITargetBlock<PinnedBuffer<float[]>> inputReleaseQueue,
                    ISourceBlock<OpusOutputBuffer> bufferQueue,
                    ITargetBlock<OpusOutputBuffer> outputQueue)
                {
                    processTask = Process(inputQueue, inputReleaseQueue, bufferQueue, outputQueue);
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
                        processCancel.Cancel();
                        try
                        {
                            processTask.Wait();
                        }
                        catch (AggregateException e)
                        {
                            foreach (var v in e.InnerExceptions)
                            {
                                Trace.WriteLine($"OpusEncode Process Exception:{e.Message} {v.Message}");
                            }
                        }
                        finally
                        {
                            processCancel.Dispose();
                        }

                        encoder.Close();
                    }

                    disposed = true;
                }

                private async Task Process(
                    ISourceBlock<PinnedBuffer<float[]>> inputQueue,
                    ITargetBlock<PinnedBuffer<float[]>> inputReleaseQueue,
                    ISourceBlock<OpusOutputBuffer> bufferQueue,
                    ITargetBlock<OpusOutputBuffer> outputQueue
                    )
                {

                    var ct = processCancel.Token;
                    await Task.Run(() =>
                    {
                        ct.ThrowIfCancellationRequested();

                        PinnedBuffer<float[]> pcm = null;

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
                                    pcm = inputQueue.Receive(new TimeSpan(1000), ct);
                                    Trace.WriteLine("[opus] receive pcm");
                                }
                                var data = bufferQueue.Receive(new TimeSpan(1000), ct);
                                Trace.WriteLine("[opus] get data");

                                data.Wrote = Interop.Opus.opus_encode(
                                    encoder,
                                    pcm.AddrOfPinnedObject(),
                                    pcm.Target().Length,
                                    data.AddrOfPinnedObject(),
                                    data.Target().Length
                                    );

                                inputReleaseQueue.Post(pcm);
                                pcm = null;
                                Trace.WriteLine("[opus] release pcm");
                                outputQueue.Post(data);
                                Trace.WriteLine("[opus] post data");
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
    }
}
