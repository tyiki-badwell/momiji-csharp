using Momiji.Core.H264;
using Momiji.Core.Opus;
using Momiji.Interop;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.Ftl
{
    public class FtlIngest : IDisposable
    {
        private bool disposed = false;
        private PinnedBuffer<Interop.Ftl.Handle> handle;
        private CancellationTokenSource logCancel = new CancellationTokenSource();
        private Task logTask;

        private long audioUsec;
        private long videoUsec;

        public FtlIngest(string streamKey)
        {
            Interop.Ftl.IngestParams param;
            param.stream_key = streamKey;
            param.video_codec = Interop.Ftl.VideoCodec.FTL_VIDEO_H264;
            //param.video_codec = Interop.Ftl.VideoCodec.FTL_VIDEO_NULL;
            param.audio_codec = Interop.Ftl.AudioCodec.FTL_AUDIO_OPUS;
            param.ingest_hostname = "auto";
            param.fps_num = 0;
            param.fps_den = 0;
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

        private int SendVideo(PinnedBuffer<Interop.Ftl.Handle> handle, H264OutputBuffer buffer)
        {
            var usec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;

            var sent = Interop.Ftl.ftl_ingest_send_media_dts(
                handle.AddrOfPinnedObject(),
                Interop.Ftl.MediaType.FTL_VIDEO_DATA,
                usec,
                buffer.AddrOfPinnedObject(),
                buffer.Wrote,
                buffer.EndOfFrame ? 1: 0
            );
            //Trace.WriteLine($"ftl_ingest_send_media_dts(FTL_VIDEO_DATA, {usec}:{buffer.Wrote}):{sent} / {(usec - videoUsec) / 1000}");
            videoUsec = usec;
            return sent;
        }


        private int SendAudio(PinnedBuffer<Interop.Ftl.Handle> handle, OpusOutputBuffer buffer)
        {
            var usec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;

            var sent = Interop.Ftl.ftl_ingest_send_media_dts(
                handle.AddrOfPinnedObject(),
                Interop.Ftl.MediaType.FTL_AUDIO_DATA,
                usec,
                buffer.AddrOfPinnedObject(),
                buffer.Wrote,
                0
            );
        //    Trace.WriteLine($"ftl_ingest_send_media_dts(FTL_AUDIO_DATA, {usec}:{buffer.Wrote}):{sent} / {(usec - audioUsec) /1000}");
            audioUsec = usec;
            return sent;
        }

        public async Task Run(
            ISourceBlock<OpusOutputBuffer> inputQueue,
            ITargetBlock<OpusOutputBuffer> inputReleaseQueue,
            CancellationToken ct)
        {
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
                        var buffer = inputQueue.Receive(new TimeSpan(20_000_000), ct);
                        //Trace.WriteLine("[ftl] receive buffer");
                        var sent = SendAudio(handle, buffer);
                        inputReleaseQueue.Post(buffer);
                        //Trace.WriteLine("[ftl] release buffer");
                    }
                    catch (TimeoutException te)
                    {
                        Trace.WriteLine("[ftl] timeout");
                        continue;
                    }
                }
            }, ct);
        }

        public async Task Run(
            ISourceBlock<H264OutputBuffer> inputQueue,
            ITargetBlock<H264OutputBuffer> inputReleaseQueue,
            CancellationToken ct)
        {
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
                        var buffer = inputQueue.Receive(new TimeSpan(20_000_000), ct);
                        //Trace.WriteLine("[ftl] receive buffer");
                        var sent = SendVideo(handle, buffer);
                        inputReleaseQueue.Post(buffer);
                        //Trace.WriteLine("[ftl] release buffer");

                        Thread.Sleep(100);
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
                                case Interop.Ftl.StatusTypes.FTL_STATUS_NONE:
                                    {
                                        Trace.WriteLine("[ftl] NONE");
                                        break;
                                    }
                                case Interop.Ftl.StatusTypes.FTL_STATUS_LOG:
                                    {
                                        Interop.Ftl.FtlStatusLogMsg log = Marshal.PtrToStructure<Interop.Ftl.FtlStatusLogMsg>(msg + 8);
                                        Trace.WriteLine($"[ftl] LOG {log.log_level}:{log.msg}");
                                        break;
                                    }
                                case Interop.Ftl.StatusTypes.FTL_STATUS_EVENT:
                                    {
                                        Interop.Ftl.FtlStatusEventMsg eventMsg = Marshal.PtrToStructure<Interop.Ftl.FtlStatusEventMsg>(msg + 8);
                                        switch (eventMsg.type)
                                        {
                                            case Interop.Ftl.StatusEventType.FTL_STATUS_EVENT_TYPE_UNKNOWN:
                                                {
                                                    Trace.WriteLine($"[ftl] EVENT UNKNOWN [{eventMsg.error_code}][{eventMsg.reason}]");
                                                    break;
                                                }
                                            case Interop.Ftl.StatusEventType.FTL_STATUS_EVENT_TYPE_CONNECTED:
                                                {
                                                    Trace.WriteLine($"[ftl] EVENT CONNECTED [{eventMsg.error_code}][{eventMsg.reason}]");
                                                    break;
                                                }
                                            case Interop.Ftl.StatusEventType.FTL_STATUS_EVENT_TYPE_DISCONNECTED:
                                                {
                                                    Trace.WriteLine($"[ftl] EVENT DISCONNECTED [{eventMsg.error_code}][{eventMsg.reason}]");
                                                    break;
                                                }
                                            case Interop.Ftl.StatusEventType.FTL_STATUS_EVENT_TYPE_DESTROYED:
                                                {
                                                    Trace.WriteLine($"[ftl] EVENT DESTROYED [{eventMsg.error_code}][{eventMsg.reason}]");
                                                    break;
                                                }
                                            case Interop.Ftl.StatusEventType.FTL_STATUS_EVENT_INGEST_ERROR_CODE:
                                                {
                                                    Trace.WriteLine($"[ftl] EVENT INGEST_ERROR_CODE [{eventMsg.error_code}][{eventMsg.reason}]");
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case Interop.Ftl.StatusTypes.FTL_STATUS_VIDEO_PACKETS:
                                    {
                                        Interop.Ftl.FtlPacketStatsMsg packetMsg = Marshal.PtrToStructure<Interop.Ftl.FtlPacketStatsMsg>(msg + 8);
                                        Trace.WriteLine(
                                            $"[ftl] VIDEO_PACKETS packet per second[{(double)packetMsg.sent * 1000.0f / (double)packetMsg.period}]" +
                                            $" total nack requests[{packetMsg.nack_reqs}]" +
                                            $" lost[{packetMsg.lost}]" +
                                            $" recovered[{packetMsg.recovered}]" +
                                            $" late[{packetMsg.late}]"
                                            );
                                        break;
                                    }
                                case Interop.Ftl.StatusTypes.FTL_STATUS_VIDEO_PACKETS_INSTANT:
                                    {
                                        Interop.Ftl.FtlPacketStatsInstantMsg packetMsg = Marshal.PtrToStructure<Interop.Ftl.FtlPacketStatsInstantMsg>(msg + 8);
                                        Trace.WriteLine(
                                            $"[ftl] VIDEO_PACKETS_INSTANT avg transmit delay {packetMsg.avg_xmit_delay}ms (min: {packetMsg.min_xmit_delay}, max: {packetMsg.max_xmit_delay})," +
                                            $" avg rtt {packetMsg.avg_rtt}ms (min: {packetMsg.min_rtt}, max: {packetMsg.max_rtt})");
                                        break;
                                    }
                                case Interop.Ftl.StatusTypes.FTL_STATUS_AUDIO_PACKETS:
                                    {
                                        Trace.WriteLine("[ftl] AUDIO_PACKETS");
                                        break;
                                    }
                                case Interop.Ftl.StatusTypes.FTL_STATUS_VIDEO:
                                    {
                                        Interop.Ftl.FtlVideoFrameStatsMsg videoMsg = Marshal.PtrToStructure<Interop.Ftl.FtlVideoFrameStatsMsg>(msg + 8);
                                        Trace.WriteLine(
                                            $"[ftl] VIDEO Queue an average of {(double)videoMsg.frames_queued * 1000.0f / (double)videoMsg.period}f fps ({(double)videoMsg.bytes_queued / (double)videoMsg.period * 8}f kbps)," +
                                            $" sent an average of {(double)videoMsg.frames_sent * 1000.0f / (double)videoMsg.period}f fps ({(double)videoMsg.bytes_sent / (double)videoMsg.period * 8}f kbps)," +
                                            $" queue fullness {videoMsg.queue_fullness}, max frame size {videoMsg.max_frame_size}, {videoMsg.bw_throttling_count}");
                                        break;
                                    }
                                case Interop.Ftl.StatusTypes.FTL_STATUS_AUDIO:
                                    {
                                        Trace.WriteLine("[ftl] AUDIO");
                                        break;
                                    }
                                case Interop.Ftl.StatusTypes.FTL_STATUS_FRAMES_DROPPED:
                                    {
                                        Trace.WriteLine("[ftl] FRAMES_DROPPED");
                                        break;
                                    }
                                case Interop.Ftl.StatusTypes.FTL_STATUS_NETWORK:
                                    {
                                        Trace.WriteLine("[ftl] NETWORK");
                                        break;
                                    }
                                case Interop.Ftl.StatusTypes.FTL_BITRATE_CHANGED:
                                    {
                                        Trace.WriteLine("[ftl] BITRATE_CHANGED");
                                        break;
                                    }
                                default:
                                    {
                                        Trace.WriteLine($"[ftl] {type}");
                                        break;
                                    }
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
