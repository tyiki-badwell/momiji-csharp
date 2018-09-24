using Microsoft.Extensions.Logging;
using Momiji.Core.H264;
using Momiji.Core.Opus;
using Momiji.Interop;
using Momiji.Interop.Ftl;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Momiji.Core.Ftl
{
    public class FtlIngest : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private bool disposed = false;
        private PinnedBuffer<Handle> handle;
        private CancellationTokenSource logCancel = new CancellationTokenSource();
        private Task logTask;

        public FtlIngest(string streamKey, ILoggerFactory loggerFactory, Timer timer, bool connect = true)
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<FtlIngest>();
            Timer = timer;

            IngestParams param;
            param.stream_key = streamKey;
            param.video_codec = VideoCodec.FTL_VIDEO_H264;
            param.audio_codec = AudioCodec.FTL_AUDIO_OPUS;
            param.ingest_hostname = "auto";
            param.fps_num = 0;
            param.fps_den = 0;
            param.peak_kbps = 0;
            param.vendor_name = "momiji";
            param.vendor_version = "0.0.1";

            if (connect)
            {
                Status status;
                status = Handle.ftl_init();
                Logger.LogInformation($"ftl_init:{status}");

                handle = new PinnedBuffer<Handle>(new Handle());

                status = Handle.ftl_ingest_create(handle.AddrOfPinnedObject, ref param);
                Logger.LogInformation($"ftl_ingest_create:{status}");
                if (status != Status.FTL_SUCCESS)
                {
                    handle.Dispose();
                    handle = null;
                    throw new Exception($"ftl_ingest_create error:{status}");
                }
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
                if (handle != null)
                {
                    Status status;
                    status = Handle.ftl_ingest_disconnect(handle.AddrOfPinnedObject);
                    Logger.LogInformation($"ftl_ingest_disconnect:{status}");

                    status = Handle.ftl_ingest_destroy(handle.AddrOfPinnedObject);
                    Logger.LogInformation($"ftl_ingest_destroy:{status}");

                    logCancel.Cancel();
                    try
                    {
                        logTask.Wait();
                    }
                    catch (AggregateException e)
                    {
                        foreach (var v in e.InnerExceptions)
                        {
                            Logger.LogInformation($"FtlIngest Log Exception:{e.Message} {v.Message}");
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

        public void Connect()
        {
            if (handle == null)
            {
                return;
            }

            logTask = PrintTrace(logCancel, handle);

            var status = Handle.ftl_ingest_connect(handle.AddrOfPinnedObject);
            Logger.LogInformation($"ftl_ingest_connect:{status}");
            if (status != Status.FTL_SUCCESS)
            {
                throw new Exception($"ftl_ingest_connect error:{status}");
            }
        }

        public void Execute(OpusOutputBuffer source)
        {
            long time = (long)source.Log.GetFirstTime();
            source.Log.Add("[ftl] start ftl_ingest_send_media_dts AUDIO", Timer.USecDouble);

            var sent = 0;
            if (handle != null)
            {
                sent = Handle.ftl_ingest_send_media_dts(
                    handle.AddrOfPinnedObject,
                    MediaType.FTL_AUDIO_DATA,
                    time,
                    source.AddrOfPinnedObject,
                    source.Wrote,
                    0
                );
            }
            source.Log.Add($"[ftl] end ftl_ingest_send_media_dts AUDIO [{sent}][{source.Wrote}][{new DateTime(time * 10, DateTimeKind.Utc):HH:mm:ss ffffff}]", Timer.USecDouble);
            if (false)
            {
                var log = "AUDIO ";
                double? temp = null;
                source.Log.Copy().ForEach((a) =>
                {
                    var lap = temp == null ? 0 : (a.time - temp);
                    log += $"\n{a.label}:{lap},";
                    temp = a.time;
                });
                Logger.LogInformation($"[ftl] {source.Log.GetSpentTime()} {log}");
            }
            source.Log.Clear();
        }

        public void Execute(H264OutputBuffer source)
        {
            long time = (long)source.Log.GetFirstTime();
            foreach (var nuls in source.LayerNuls)
            {
                for (var idx = 0; idx < nuls.Count; idx++)
                {
                    var nul = nuls[idx];
                    var endOfFrame = (idx == nuls.Count - 1) ? 1 : 0;
                    source.Log.Add("[ftl] start ftl_ingest_send_media_dts VIDEO", Timer.USecDouble);
                    var sent = 0;
                    if (handle != null)
                    {
                        sent = Handle.ftl_ingest_send_media_dts(
                            handle.AddrOfPinnedObject,
                            MediaType.FTL_VIDEO_DATA,
                            time,
                            source.AddrOfPinnedObject + nul.offset,
                            nul.length,
                            endOfFrame
                        );
                    }
                    source.Log.Add($"[ftl] end ftl_ingest_send_media_dts VIDEO [{sent}][{nul.length}][{endOfFrame}][{new DateTime(time * 10, DateTimeKind.Utc):HH:mm:ss ffffff}]", Timer.USecDouble);
                    time++;
                }
            }
            if (false)
            {
                var log = "VIDEO ";
                double? temp = null;
                source.Log.Copy().ForEach((a) =>
                {
                    var lap = temp == null ? 0 : (a.time - temp);
                    log += $"\n{a.label}:{lap},";
                    temp = a.time;
                });
                Logger.LogInformation($"[ftl] {source.Log.GetSpentTime()} {log}");
            }
            source.Log.Clear();
        }

        private async Task PrintTrace(CancellationTokenSource logCancel, PinnedBuffer<Handle> handle)
        {
            CancellationToken ct = logCancel.Token;

            using (var buffer = new PinnedBuffer<byte[]>(new byte[2048]))
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var msg = buffer.AddrOfPinnedObject;

                    while (true)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            ct.ThrowIfCancellationRequested();
                        }

                        var status = Handle.ftl_ingest_get_status(handle.AddrOfPinnedObject, msg, 500);
                        if (status != Status.FTL_SUCCESS)
                        {
                            continue;
                        }

                        var type = (StatusTypes)Marshal.ReadInt32(msg, 0);
                        switch (type)
                        {
                            case StatusTypes.FTL_STATUS_NONE:
                                {
                                    Logger.LogInformation("[ftl] NONE");
                                    break;
                                }
                            case StatusTypes.FTL_STATUS_LOG:
                                {
                                    FtlStatusLogMsg log = Marshal.PtrToStructure<FtlStatusLogMsg>(msg + 8);
                                    Logger.LogInformation($"[ftl] LOG {log.log_level}:{log.msg}");
                                    break;
                                }
                            case StatusTypes.FTL_STATUS_EVENT:
                                {
                                    FtlStatusEventMsg eventMsg = Marshal.PtrToStructure<FtlStatusEventMsg>(msg + 8);
                                    switch (eventMsg.type)
                                    {
                                        case StatusEventType.FTL_STATUS_EVENT_TYPE_UNKNOWN:
                                            {
                                                Logger.LogInformation($"[ftl] EVENT UNKNOWN [{eventMsg.error_code}][{eventMsg.reason}]");
                                                break;
                                            }
                                        case StatusEventType.FTL_STATUS_EVENT_TYPE_CONNECTED:
                                            {
                                                Logger.LogInformation($"[ftl] EVENT CONNECTED [{eventMsg.error_code}][{eventMsg.reason}]");
                                                break;
                                            }
                                        case StatusEventType.FTL_STATUS_EVENT_TYPE_DISCONNECTED:
                                            {
                                                Logger.LogInformation($"[ftl] EVENT DISCONNECTED [{eventMsg.error_code}][{eventMsg.reason}]");
                                                break;
                                            }
                                        case StatusEventType.FTL_STATUS_EVENT_TYPE_DESTROYED:
                                            {
                                                Logger.LogInformation($"[ftl] EVENT DESTROYED [{eventMsg.error_code}][{eventMsg.reason}]");
                                                break;
                                            }
                                        case StatusEventType.FTL_STATUS_EVENT_INGEST_ERROR_CODE:
                                            {
                                                Logger.LogInformation($"[ftl] EVENT INGEST_ERROR_CODE [{eventMsg.error_code}][{eventMsg.reason}]");
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case StatusTypes.FTL_STATUS_VIDEO_PACKETS:
                                {
                                    FtlPacketStatsMsg packetMsg = Marshal.PtrToStructure<FtlPacketStatsMsg>(msg + 8);
                                    Logger.LogInformation(
                                        $"[ftl] VIDEO_PACKETS packet per second[{(double)packetMsg.sent * 1000.0f / (double)packetMsg.period}]" +
                                        $" total nack requests[{packetMsg.nack_reqs}]" +
                                        $" lost[{packetMsg.lost}]" +
                                        $" recovered[{packetMsg.recovered}]" +
                                        $" late[{packetMsg.late}]"
                                        );
                                    break;
                                }
                            case StatusTypes.FTL_STATUS_VIDEO_PACKETS_INSTANT:
                                {
                                    FtlPacketStatsInstantMsg packetMsg = Marshal.PtrToStructure<FtlPacketStatsInstantMsg>(msg + 8);
                                    Logger.LogInformation(
                                        $"[ftl] VIDEO_PACKETS_INSTANT avg transmit delay {packetMsg.avg_xmit_delay}ms (min: {packetMsg.min_xmit_delay}, max: {packetMsg.max_xmit_delay})," +
                                        $" avg rtt {packetMsg.avg_rtt}ms (min: {packetMsg.min_rtt}, max: {packetMsg.max_rtt})");
                                    break;
                                }
                            case StatusTypes.FTL_STATUS_AUDIO_PACKETS:
                                {
                                    Logger.LogInformation("[ftl] AUDIO_PACKETS");
                                    break;
                                }
                            case StatusTypes.FTL_STATUS_VIDEO:
                                {
                                    FtlVideoFrameStatsMsg videoMsg = Marshal.PtrToStructure<FtlVideoFrameStatsMsg>(msg + 8);
                                    Logger.LogInformation(
                                        $"[ftl] VIDEO Queue an average of {(double)videoMsg.frames_queued * 1000.0f / (double)videoMsg.period}f fps ({(double)videoMsg.bytes_queued / (double)videoMsg.period * 8}f kbps)," +
                                        $" sent an average of {(double)videoMsg.frames_sent * 1000.0f / (double)videoMsg.period}f fps ({(double)videoMsg.bytes_sent / (double)videoMsg.period * 8}f kbps)," +
                                        $" queue fullness {videoMsg.queue_fullness}, max frame size {videoMsg.max_frame_size}, {videoMsg.bw_throttling_count}");
                                    break;
                                }
                            case StatusTypes.FTL_STATUS_AUDIO:
                                {
                                    Logger.LogInformation("[ftl] AUDIO");
                                    break;
                                }
                            case StatusTypes.FTL_STATUS_FRAMES_DROPPED:
                                {
                                    Logger.LogInformation("[ftl] FRAMES_DROPPED");
                                    break;
                                }
                            case StatusTypes.FTL_STATUS_NETWORK:
                                {
                                    Logger.LogInformation("[ftl] NETWORK");
                                    break;
                                }
                            case StatusTypes.FTL_BITRATE_CHANGED:
                                {
                                    Logger.LogInformation("[ftl] BITRATE_CHANGED");
                                    break;
                                }
                            default:
                                {
                                    Logger.LogInformation($"[ftl] {type}");
                                    break;
                                }
                        }
                    }
                }, ct);
            }
        }
    }
}
