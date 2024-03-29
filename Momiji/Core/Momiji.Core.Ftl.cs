﻿using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Core.H264;
using Momiji.Core.Opus;
using Momiji.Core.Timer;
using Momiji.Interop.Ftl;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Momiji.Core.Ftl;

public class FtlException : Exception
{
    public FtlException()
    {
    }

    public FtlException(string message) : base(message)
    {
    }

    public FtlException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class FtlIngest : IDisposable
{
    private ILoggerFactory LoggerFactory { get; }
    private ILogger Logger { get; }
    private ElapsedTimeCounter Counter { get; }
    private long AudioInterval { get; }
    private long VideoInterval { get; }

    private bool disposed;
    private PinnedBuffer<FtlHandle>? handle;
    private readonly CancellationTokenSource logCancel = new();
    private Task? logTask;

    private long lastAudioUsec;
    private long lastVideoUsec;

    public FtlIngest(
        string streamKey,
        string ingestHostname,
        ILoggerFactory loggerFactory,
        ElapsedTimeCounter counter,
        long audioInterval,
        long videoInterval,
        string? mixerApiClientId = default,
        string? caInfoPath = default
    )
    {
        LoggerFactory = loggerFactory;
        Logger = LoggerFactory.CreateLogger<FtlIngest>();
        Counter = counter;
        AudioInterval = audioInterval;
        VideoInterval = videoInterval;

        IngestParams param;
        param.stream_key = streamKey ?? throw new ArgumentNullException(nameof(streamKey));
        param.video_codec = VideoCodec.FTL_VIDEO_H264;
        param.audio_codec = AudioCodec.FTL_AUDIO_OPUS;
        param.ingest_hostname = ingestHostname ?? throw new ArgumentNullException(nameof(ingestHostname));
        param.fps_num = 0;
        param.fps_den = 0;
        param.peak_kbps = 0;
        param.vendor_name = "momiji";
        param.vendor_version = "0.0.1";
        /*
        param.ca_info_path = caInfoPath;
        param.mixer_api_client_id = mixerApiClientId;
        */

        {
            Status status;
            status = NativeMethods.ftl_init();
            Logger.LogInformation($"ftl_init:{status}");

            handle = new PinnedBuffer<FtlHandle>(new FtlHandle());

            status = NativeMethods.ftl_ingest_create(handle.AddrOfPinnedObject, ref param);
            Logger.LogInformation($"ftl_ingest_create:{status}");
            if (status != Status.FTL_SUCCESS)
            {
                handle.Dispose();
                throw new FtlException($"ftl_ingest_create error:{status}");
            }

            logTask = PrintTrace();
        }
    }

    ~FtlIngest()
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
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (disposing)
        {
        }

        if (handle != null)
        {
            Status status;
            status = NativeMethods.ftl_ingest_disconnect(handle.AddrOfPinnedObject);
            Logger.LogInformation($"ftl_ingest_disconnect:{status}");

            logCancel.Cancel();
            if (logTask != null)
            {
                try
                {
                    logTask.Wait();
                }
                catch (AggregateException e)
                {
                    Logger.LogInformation(e, "FtlIngest Log Exception");
                }
                finally
                {
                    logCancel.Dispose();
                }
                logTask = null;
            }

            status = NativeMethods.ftl_ingest_destroy(handle.AddrOfPinnedObject);
            Logger.LogInformation($"ftl_ingest_destroy:{status}");

            handle.Dispose();
            handle = null;
        }
    }

    public void Connect()
    {
        if (handle == null)
        {
            return;
        }

        var status = NativeMethods.ftl_ingest_connect(handle.AddrOfPinnedObject);
        Logger.LogInformation($"ftl_ingest_connect:{status}");
        if (status != Status.FTL_SUCCESS)
        {
            throw new FtlException($"ftl_ingest_connect error:{status}");
        }
    }

    public void Execute(OpusOutputBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Buffer == null)
        {
            throw new InvalidOperationException("source.Buffer is null.");
        }

        var now = Counter.NowTicks / 10;
        if (lastAudioUsec == default)
        {
            lastAudioUsec = now;
        }
        else
        {
            lastAudioUsec += AudioInterval;
            Logger.LogDebug($"[ftl] AUDIO delay {now - lastAudioUsec} {now - source.Log.FirstTime()}");
        }

        var time = lastAudioUsec;
        source.Log.Add("[ftl] start ftl_ingest_send_media_dts AUDIO", Counter.NowTicks);

        var sent = 0;
        if (handle != null)
        {
            sent = NativeMethods.ftl_ingest_send_media_dts(
                handle.AddrOfPinnedObject,
                MediaType.FTL_AUDIO_DATA,
                time,
                source.Buffer.AddrOfPinnedObject,
                source.Wrote,
                0
            );
        }
        source.Log.Add($"[ftl] end ftl_ingest_send_media_dts AUDIO [{sent}][{source.Wrote}][{new DateTime(time * 10, DateTimeKind.Utc):HH:mm:ss ffffff}]", Counter.NowTicks);

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            var log = "AUDIO ";
            double? temp = null;
            source.Log.ForEach((a) =>
            {
                var lap = temp.HasValue ? (a.time - temp) : 0;
                log += $"\n[{ new DateTime((long)(a.time * 10), DateTimeKind.Utc):HH:mm:ss ffffff}][{lap:0000000000.000}]{a.label}";
                temp = a.time;
            });
            Logger.LogDebug($"[ftl] {source.Log.SpentTime()} {log}");
        }

        source.Log.Clear();
    }

    public void Execute(H264OutputBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Buffer == null)
        {
            throw new InvalidOperationException("source.Buffer is null.");
        }

        var now = Counter.NowTicks / 10;
        if (lastVideoUsec == default)
        {
            lastVideoUsec = now;
        }
        else
        {
            lastVideoUsec += VideoInterval;
            Logger.LogDebug($"[ftl] VIDEO delay {now - lastVideoUsec} {now - source.Log.FirstTime()}");
        }

        var time = lastVideoUsec;

        foreach (var nuls in source.LayerNuls)
        {
            for (var idx = 0; idx < nuls.Count; idx++)
            {
                var (offset, length) = nuls[idx];
                var endOfFrame = (idx == nuls.Count - 1) ? 1 : 0;
                source.Log.Add("[ftl] start ftl_ingest_send_media_dts VIDEO", Counter.NowTicks);
                var sent = 0;
                if (handle != null)
                {
                    sent = NativeMethods.ftl_ingest_send_media_dts(
                        handle.AddrOfPinnedObject,
                        MediaType.FTL_VIDEO_DATA,
                        time,
                        source.Buffer.AddrOfPinnedObject + offset,
                        length,
                        endOfFrame
                    );
                }
                source.Log.Add($"[ftl] end ftl_ingest_send_media_dts VIDEO [{sent}][{length}][{endOfFrame}][{new DateTime(time * 10, DateTimeKind.Utc):HH:mm:ss ffffff}]", Counter.NowTicks);
                time++;
            }
        }

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            var log = "VIDEO ";
            double? temp = null;
            source.Log.ForEach((a) =>
            {
                var lap = temp.HasValue ? (a.time - temp) : 0;
                log += $"\n[{ new DateTime((long)(a.time * 10), DateTimeKind.Utc):HH:mm:ss ffffff}][{lap:0000000000.000}]{a.label}";
                temp = a.time;
            });
            Logger.LogDebug($"[ftl] {source.Log.SpentTime()} {log}");
        }

        source.Log.Clear();
    }

    private async Task PrintTrace()
    {
        var ct = logCancel.Token;

        using var buffer = new PinnedBuffer<byte[]>(new byte[2048]);
        try
        {
            await Task.Run(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var msg = buffer.AddrOfPinnedObject;

                while (true)
                {
                    if (handle == null)
                    {
                        break;
                    }

                    var status = NativeMethods.ftl_ingest_get_status(handle.AddrOfPinnedObject, msg, 500);
                    if (status != Status.FTL_SUCCESS)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            // メッセージが無くなったら脱出する
                            break;
                        }
                        continue;
                    }

                    ref var statusMsg = ref Unsafe.As<byte, FtlStatusMsg>(ref buffer.Target[0]);
                    switch (statusMsg.status)
                    {
                        case StatusTypes.FTL_STATUS_NONE:
                            {
                                Logger.LogInformation("[ftl] NONE");
                                break;
                            }
                        case StatusTypes.FTL_STATUS_LOG:
                            {
                                //stringがあるのでUnsafe.Asできない。残念。
                                var log = Marshal.PtrToStructure<FtlStatusLogMsg>(msg);
                                Logger.LogInformation($"[ftl] LOG {log.log_level}:{log.msg}");
                                break;
                            }
                        case StatusTypes.FTL_STATUS_EVENT:
                            {
                                ref var eventMsg = ref Unsafe.As<FtlStatusMsg, FtlStatusEventMsg>(ref statusMsg);
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
                                            if (eventMsg.reason == StatusEventReasons.FTL_STATUS_EVENT_REASON_API_REQUEST)
                                            {
                                                goto EXIT;
                                            }
                                            if (!disposed)
                                            {
                                                Connect();
                                            }
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
                                ref var packetMsg = ref Unsafe.As<FtlStatusMsg, FtlPacketStatsMsg>(ref statusMsg);
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
                                ref var packetMsg = ref Unsafe.As<FtlStatusMsg, FtlPacketStatsInstantMsg>(ref statusMsg);
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
                                ref var videoMsg = ref Unsafe.As<FtlStatusMsg, FtlVideoFrameStatsMsg>(ref statusMsg);
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
                                Logger.LogInformation($"[ftl] {statusMsg.status}");
                                break;
                            }
                    }
                }
            EXIT:;
            }, ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.LogInformation(e, "Exception");
            throw;
        }
        finally
        {
            Logger.LogInformation("status loop end");
        }
    }
}
