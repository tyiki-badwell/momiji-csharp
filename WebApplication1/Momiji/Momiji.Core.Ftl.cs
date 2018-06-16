using Momiji.Interop;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using static Momiji.Core.Opus;

namespace Momiji
{
    namespace Core
    {
        public class Ftl
        {
            public class FtlIngest : IDisposable
            {
                private bool disposed = false;
                private PinnedBuffer<Interop.Ftl.Handle> handle;
                private CancellationTokenSource processCancel = new CancellationTokenSource();
                private Task processTask;
                private CancellationTokenSource logCancel = new CancellationTokenSource();
                private Task logTask;

                public FtlIngest(string streamKey)
                {
                    Interop.Ftl.IngestParams param;
                    param.stream_key = streamKey;
                    param.video_codec = Interop.Ftl.VideoCodec.FTL_VIDEO_H264;
                    param.audio_codec = Interop.Ftl.AudioCodec.FTL_AUDIO_OPUS;
                    param.ingest_hostname = "auto";
                    param.fps_num = 24;
                    param.fps_den = 1;
                    param.peak_kbps = 0;
                    param.vendor_name = "momiji";
                    param.vendor_version = "0.0.1";

                    Interop.Ftl.Status status;
                    status = Interop.Ftl.ftl_init();
                    Trace.WriteLine($"ftl_init:{status}");

                    handle = new PinnedBuffer<Interop.Ftl.Handle>(new Interop.Ftl.Handle());

                    status = Interop.Ftl.ftl_ingest_create(handle.AddrOfPinnedObject(), ref param);
                    Trace.WriteLine($"ftl_ingest_create:{status}");
                    if (status != Interop.Ftl.Status.FTL_SUCCESS)
                    {
                        handle.Dispose();
                        handle = null;
                        throw new Exception($"ftl_ingest_create error:{status}");
                    }

                    logCancel = new CancellationTokenSource();
                    logTask = PrintTrace(logCancel, handle);

                    status = Interop.Ftl.ftl_ingest_connect(handle.AddrOfPinnedObject());
                    Trace.WriteLine($"ftl_ingest_connect:{status}");
                }

                public void Run(
                    ISourceBlock<OpusOutputBuffer> inputQueue,
                    ITargetBlock<OpusOutputBuffer> inputReleaseQueue)
                {
                    processTask = Process(inputQueue, inputReleaseQueue);
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
                                Trace.WriteLine($"FtlIngest Process Exception:{e.Message} {v.Message}");
                            }
                        }
                        finally
                        {
                            processCancel.Dispose();
                        }

                        if (handle != null)
                        {
                            Interop.Ftl.Status status;
                            status = Interop.Ftl.ftl_ingest_disconnect(handle.AddrOfPinnedObject());
                            Trace.WriteLine($"ftl_ingest_disconnect:{status}");

                            status = Interop.Ftl.ftl_ingest_destroy(handle.AddrOfPinnedObject());
                            Trace.WriteLine($"ftl_ingest_destroy:{status}");

                            logCancel.Cancel();
                            try
                            {
                                logTask.Wait();
                            }
                            catch (AggregateException e)
                            {
                                foreach (var v in e.InnerExceptions)
                                {
                                    Trace.WriteLine($"FtlIngest Log Exception:{e.Message} {v.Message}");
                                }
                            }
                            finally
                            {
                                logCancel.Dispose();
                            }

                            handle.Dispose();
                            handle = null;
                        }
                    }

                    disposed = true;
                }

                private static DateTime UNIX_EPOCH = new DateTime(1970, 1, 1, 0, 0, 0, 0);

                public Interop.Ftl.Status SendVideo(PinnedBuffer<Interop.Ftl.Handle> handle, PinnedBuffer<byte[]> buffer)
                {
                    var now = DateTime.UtcNow;
                    var usec = ((long)(now - UNIX_EPOCH).TotalSeconds * 1000000) + (now.Millisecond * 1000);

                    var status = Interop.Ftl.ftl_ingest_send_media_dts(
                        handle.AddrOfPinnedObject(),
                        Interop.Ftl.MediaType.FTL_VIDEO_DATA,
                        usec,
                        buffer.AddrOfPinnedObject(),
                        2048,
                        0
                    );
                    Trace.WriteLine($"ftl_ingest_send_media_dts(FTL_VIDEO_DATA):{status}");
                    return status;
                }


                public Interop.Ftl.Status SendAudio(PinnedBuffer<Interop.Ftl.Handle> handle, OpusOutputBuffer buffer)
                {
                    var now = DateTime.UtcNow;
                    var usec = ((long)(now - UNIX_EPOCH).TotalSeconds * 1000000) + (now.Millisecond * 1000);

                    var status = Interop.Ftl.ftl_ingest_send_media_dts(
                        handle.AddrOfPinnedObject(),
                        Interop.Ftl.MediaType.FTL_AUDIO_DATA,
                        usec,
                        buffer.AddrOfPinnedObject(),
                        buffer.Wrote,
                        0
                    );
                    Trace.WriteLine($"ftl_ingest_send_media_dts(FTL_AUDIO_DATA, {buffer.Wrote}):{status}");
                    return status;
                }

                private async Task Process(
                    ISourceBlock<OpusOutputBuffer> inputQueue,
                    ITargetBlock<OpusOutputBuffer> inputReleaseQueue)
                {

                    var ct = processCancel.Token;

                    await Task.Run(() =>
                    {
                        ct.ThrowIfCancellationRequested();

                        int a = 0;
                        while (true)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                break;
                            }
                            try
                            {
                                var buffer = inputQueue.Receive(new TimeSpan(2000), ct);
                                Trace.WriteLine("[ftl] receive buffer");

                                var audioStatus = SendAudio(handle, buffer);

                                //var videoStatus = SendVideo(handle, buffer);

                                inputReleaseQueue.Post(buffer);
                                Trace.WriteLine("[ftl] release buffer");
                            }
                            catch (TimeoutException te)
                            {
                                Trace.WriteLine("[ftl] timeout");
                                continue;
                            }
                        }
                    }, ct);
                }

                private async Task PrintTrace(CancellationTokenSource logCancel, PinnedBuffer<Interop.Ftl.Handle> handle)
                {
                    CancellationToken ct = logCancel.Token;

                    Trace.WriteLine("trace start");
                    try
                    {
                        Interop.Ftl.Status status;
                        using (var buffer = new PinnedBuffer<byte[]>(new byte[2048]))
                        {
                            await Task.Run(() =>
                            {
                                ct.ThrowIfCancellationRequested();

                                var msg = buffer.AddrOfPinnedObject();

                                while (true)
                                {
                                    if (ct.IsCancellationRequested)
                                    {
                                        ct.ThrowIfCancellationRequested();
                                    }

                                    status = Interop.Ftl.ftl_ingest_get_status(handle.AddrOfPinnedObject(), msg, 500);
                                    if (status != Interop.Ftl.Status.FTL_SUCCESS)
                                    {
                                        continue;
                                    }

                                    var type = (Interop.Ftl.StatusTypes)Marshal.ReadInt32(msg, 0);
                                    switch (type)
                                    {
                                        case Interop.Ftl.StatusTypes.FTL_STATUS_LOG:
                                            Interop.Ftl.FtlStatusLogMsg log = Marshal.PtrToStructure<Interop.Ftl.FtlStatusLogMsg>(msg + 8);
                                            Trace.WriteLine($"{log.log_level}:{log.msg}");
                                            break;

                                        default:
                                            Trace.WriteLine(type);
                                            break;
                                    }
                                }
                            }, ct);
                        }
                    }
                    finally
                    {
                        Trace.WriteLine("trace end");
                    }
                }
            }
        }
    }
}
