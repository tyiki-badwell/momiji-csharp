using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading.Tasks;
using Momiji.Interop;
using System.Threading;
using System.Security.Permissions;

namespace Momiji
{
    namespace Core
    {
        public class Ftl
        {
            public class FtlIngest : IDisposable
            {
                private bool disposed = false;
                private CancellationTokenSource processCancel = new CancellationTokenSource();
                private Task processTask;

                public FtlIngest(ref Interop.Ftl.IngestParams param)
                {
                    processTask = HostProcess(param);
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
                                Trace.WriteLine("FtlIngest Process Exception:" + e.Message + " " + v.Message);
                            }
                        }
                        finally
                        {
                            processCancel.Dispose();
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
                    Trace.WriteLine("ftl_ingest_send_media_dts(FTL_VIDEO_DATA):" + status);
                    return status;
                }


                public Interop.Ftl.Status SendAudio(PinnedBuffer<Interop.Ftl.Handle> handle, PinnedBuffer<byte[]> buffer)
                {
                    var now = DateTime.UtcNow;
                    var usec = ((long)(now - UNIX_EPOCH).TotalSeconds * 1000000) + (now.Millisecond * 1000);

                    var status = Interop.Ftl.ftl_ingest_send_media_dts(
                        handle.AddrOfPinnedObject(),
                        Interop.Ftl.MediaType.FTL_AUDIO_DATA,
                        usec,
                        buffer.AddrOfPinnedObject(),
                        2048,
                        0
                    );
                    Trace.WriteLine("ftl_ingest_send_media_dts(FTL_AUDIO_DATA):" + status);
                    return status;
                }

                private async Task HostProcess(Interop.Ftl.IngestParams param)
                {
                    Interop.Ftl.Status status;
                    status = Interop.Ftl.ftl_init();
                    Trace.WriteLine("ftl_init:" + status);

                    var handle = new PinnedBuffer<Interop.Ftl.Handle>(new Interop.Ftl.Handle());

                    status = Interop.Ftl.ftl_ingest_create(handle.AddrOfPinnedObject(), ref param);
                    Trace.WriteLine("ftl_ingest_create:" + status);

                    var logCancel = new CancellationTokenSource();
                    var logTask = PrintTrace(logCancel, handle);

                    status = Interop.Ftl.ftl_ingest_connect(handle.AddrOfPinnedObject());
                    Trace.WriteLine("ftl_ingest_connect:" + status);

                    var ct = processCancel.Token;

                    using (var buffer = new PinnedBuffer<byte[]>(new byte[2048]))
                    {
                        await Task.Run(() =>
                        {
                            ct.ThrowIfCancellationRequested();

                            int a = 0;
                            while (true)
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    ct.ThrowIfCancellationRequested();
                                }

                                var audioStatus = SendAudio(handle, buffer);
                                var videoStatus = SendVideo(handle, buffer);

                                Trace.WriteLine("loop:" + a++);
                                Thread.Sleep(50);
                            }
                        }, ct);
                    }
                    status = Interop.Ftl.ftl_ingest_disconnect(handle.AddrOfPinnedObject());
                    Trace.WriteLine("ftl_ingest_disconnect:" + status);

                    status = Interop.Ftl.ftl_ingest_destroy(handle.AddrOfPinnedObject());
                    Trace.WriteLine("ftl_ingest_destroy:" + status);

                    logCancel.Cancel();
                    try
                    { 
                        logTask.Wait();
                    }
                    catch (AggregateException e)
                    {
                        foreach (var v in e.InnerExceptions)
                        {
                            Trace.WriteLine("FtlIngest Log Exception:" + e.Message + " " + v.Message);
                        }
                    }
                    finally
                    {
                        logCancel.Dispose();
                    }
                }

                private async Task PrintTrace(CancellationTokenSource logCancel, PinnedBuffer<Interop.Ftl.Handle> handle)
                {
                    CancellationToken ct = logCancel.Token;

                    Trace.WriteLine("trace start");
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
                                        Trace.WriteLine(log.log_level + ":" + log.msg);
                                        break;

                                    default:
                                        Trace.WriteLine(type);
                                        break;
                                }
                            }
                        }, ct);
                    }
                    Trace.WriteLine("trace end");
                }
            }
        }
    }
}
