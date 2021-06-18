using System;
using System.Runtime.InteropServices;

#pragma warning disable CA1707 // 識別子はアンダースコアを含むことはできません
#pragma warning disable CA1815 // equals および operator equals を値型でオーバーライドします
#pragma warning disable CA1051 // 参照可能なインスタンス フィールドを宣言しません

namespace Momiji.Interop.Ftl
{
    public enum Status : int
    {
        FTL_SUCCESS,                  /**< Operation was successful */
        FTL_SOCKET_NOT_CONNECTED,
        FTL_NON_ZERO_POINTER,         /**< Function required a zero-ed pointer, but didn't get one */
        FTL_MALLOC_FAILURE,           /**< memory allocation failed */
        FTL_DNS_FAILURE,              /**< DNS probe failed */
        FTL_CONNECT_ERROR,            /**< Failed to connect to ingest */
        FTL_INTERNAL_ERROR,           /**< Got valid inputs, but FTL failed to complete the action due to internal failure */
        FTL_CONFIG_ERROR,             /**< The configuration supplied was invalid or incomplete */
        FTL_STREAM_REJECTED,          /**< Ingest rejected our connect command */
        FTL_NOT_ACTIVE_STREAM,        /**< The function required an active stream and was passed an inactive one */
        FTL_UNAUTHORIZED,             /**< Parameters were correct, but streamer not authorized to use FTL */
        FTL_AUDIO_SSRC_COLLISION,     /**< The audio SSRC from this IP is currently in use */
        FTL_VIDEO_SSRC_COLLISION,     /**< The video SSRC from this IP is currently in use */
        FTL_BAD_REQUEST,              /**< Ingest didn't like our request. Should never happen */
        FTL_OLD_VERSION,              /**< libftl needs to be updated */
        FTL_BAD_OR_INVALID_STREAM_KEY,
        FTL_UNSUPPORTED_MEDIA_TYPE,
        FTL_GAME_BLOCKED,             /**< The current game set by this profile can't be streamed. */
        FTL_NOT_CONNECTED,
        FTL_ALREADY_CONNECTED,
        FTL_UNKNOWN_ERROR_CODE,
        FTL_STATUS_TIMEOUT,
        FTL_QUEUE_FULL,
        FTL_STATUS_WAITING_FOR_KEY_FRAME,
        FTL_QUEUE_EMPTY,
        FTL_NOT_INITIALIZED,
        FTL_CHANNEL_IN_USE,           /**< The channel is already actively streaming */
        FTL_REGION_UNSUPPORTED,       /**< The region you are attempting to stream from is not authorized to stream by local governments */
        FTL_NO_MEDIA_TIMEOUT,
        FTL_USER_DISCONNECT,
        FTL_INGEST_NO_RESPONSE,
        FTL_NO_PING_RESPONSE,
        FTL_SPEED_TEST_ABORTED,
        FTL_INGEST_SOCKET_CLOSED,
        FTL_INGEST_SOCKET_TIMEOUT,
    }

    public enum ConnectionStatus : int
    {
        FTL_CONNECTION_DISCONNECTED,
        FTL_CONNECTION_RECONNECTED
    }

    public enum VideoCodec : int
    {
        FTL_VIDEO_NULL, /**< No video for this stream */
        FTL_VIDEO_VP8,  /**< Google's VP8 codec (recommended default) */
        FTL_VIDEO_H264
    }

    public enum AudioCodec : int
    {
        FTL_AUDIO_NULL, /**< No audio for this stream */
        FTL_AUDIO_OPUS, /**< Xiph's Opus audio codec */
        FTL_AUDIO_AAC
    }

    public enum MediaType : int
    {
        FTL_AUDIO_DATA,
        FTL_VIDEO_DATA
    }

    public enum LogSeverity : int
    {
        FTL_LOG_CRITICAL,
        FTL_LOG_ERROR,
        FTL_LOG_WARN,
        FTL_LOG_INFO,
        FTL_LOG_DEBUG
    }

    public enum StatusTypes : int
    {
        FTL_STATUS_NONE,
        FTL_STATUS_LOG,
        FTL_STATUS_EVENT,
        FTL_STATUS_VIDEO_PACKETS,
        FTL_STATUS_VIDEO_PACKETS_INSTANT,
        FTL_STATUS_AUDIO_PACKETS,
        FTL_STATUS_VIDEO,
        FTL_STATUS_AUDIO,
        FTL_STATUS_FRAMES_DROPPED,
        FTL_STATUS_NETWORK,
        FTL_BITRATE_CHANGED
    }

    public enum StatusEventType : int
    {
        FTL_STATUS_EVENT_TYPE_UNKNOWN,
        FTL_STATUS_EVENT_TYPE_CONNECTED,
        FTL_STATUS_EVENT_TYPE_DISCONNECTED,
        FTL_STATUS_EVENT_TYPE_DESTROYED,
        FTL_STATUS_EVENT_INGEST_ERROR_CODE
    }

    public enum StatusEventReasons : int
    {
        FTL_STATUS_EVENT_REASON_NONE,
        FTL_STATUS_EVENT_REASON_NO_MEDIA,
        FTL_STATUS_EVENT_REASON_API_REQUEST,
        FTL_STATUS_EVENT_REASON_UNKNOWN,
    }

    public enum BitrateChangedType : int
    {
        FTL_BITRATE_DECREASED,
        FTL_BITRATE_INCREASED,
        FTL_BITRATE_STABILIZED
    }

    public enum BitrateChangedReason : int
    {
        FTL_BANDWIDTH_CONSTRAINED,
        FTL_UPGRADE_EXCESSIVE,
        FTL_BANDWIDTH_AVAILABLE,
        FTL_STABILIZE_ON_LOWER_BITRATE,
        FTL_STABILIZE_ON_ORIGINAL_BITRATE,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct IngestParams
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string ingest_hostname;
        [MarshalAs(UnmanagedType.LPStr)]
        public string stream_key;
        public VideoCodec video_codec;
        public AudioCodec audio_codec;
        public int peak_kbps; //used for the leaky bucket to smooth out packet flow rate, set to 0 to bypass
        public int fps_num;
        public int fps_den;
        [MarshalAs(UnmanagedType.LPStr)]
        public string vendor_name;
        [MarshalAs(UnmanagedType.LPStr)]
        public string vendor_version;
        /*
        [MarshalAs(UnmanagedType.LPStr)]
        public string ca_info_path;
        [MarshalAs(UnmanagedType.LPStr)]
        public string mixer_api_client_id;
        */
    }

    internal static class Libraries
    {
        public const string Ftl = "ftl.dll";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal class FtlHandle
    {
        public IntPtr priv;
    }

    internal static class SafeNativeMethods
    {
        //FTL_API ftl_status_t ftl_init();
        [DllImport(Libraries.Ftl, CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5393:安全でない DllImportSearchPath 値を使用しない", Justification = "<保留中>")]
        internal static extern Status ftl_init();

        //FTL_API ftl_status_t ftl_ingest_create(ftl_handle_t* ftl_handle, ftl_ingest_params_t*params);
        [DllImport(Libraries.Ftl, CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5393:安全でない DllImportSearchPath 値を使用しない", Justification = "<保留中>")]
        internal static extern Status ftl_ingest_create(
            [In] IntPtr ftl_handle,
            [In] ref IngestParams _params
        );

        //FTL_API ftl_status_t ftl_ingest_connect(ftl_handle_t* ftl_handle);
        [DllImport(Libraries.Ftl, CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5393:安全でない DllImportSearchPath 値を使用しない", Justification = "<保留中>")]
        internal static extern Status ftl_ingest_connect(
            [In] IntPtr ftl_handle
        );

        //FTL_API int ftl_ingest_speed_test(ftl_handle_t* ftl_handle, int speed_kbps, int duration_ms);
        [DllImport(Libraries.Ftl, CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5393:安全でない DllImportSearchPath 値を使用しない", Justification = "<保留中>")]
        internal static extern Status ftl_ingest_speed_test(
            [In] IntPtr ftl_handle,
            [In] int speed_kbps,
            [In] int duration_ms
        );

        //FTL_API ftl_status_t ftl_ingest_speed_test_ex(ftl_handle_t* ftl_handle, int speed_kbps, int duration_ms, speed_test_t* results);

        //FTL_API int ftl_ingest_send_media_dts(ftl_handle_t* ftl_handle, ftl_media_type_t media_type, int64_t dts_usec, uint8_t* data, int32_t len, int end_of_frame);
        [DllImport(Libraries.Ftl, CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5393:安全でない DllImportSearchPath 値を使用しない", Justification = "<保留中>")]
        internal static extern int ftl_ingest_send_media_dts(
            [In] IntPtr ftl_handle,
            [In] MediaType media_type,
            [In] long dts_usec,
            [In] IntPtr data,
            [In] int len,
            [In] int end_of_frame
        );

        //FTL_API ftl_status_t ftl_ingest_get_status(ftl_handle_t* ftl_handle, ftl_status_msg_t* msg, int ms_timeout);
        [DllImport(Libraries.Ftl, CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5393:安全でない DllImportSearchPath 値を使用しない", Justification = "<保留中>")]
        internal static extern Status ftl_ingest_get_status(
            [In] IntPtr ftl_handle,
            [In] IntPtr msg,
            [In] int msTimeout
        );

        //FTL_API ftl_status_t ftl_ingest_update_params(ftl_handle_t* ftl_handle, ftl_ingest_params_t*params);

        //FTL_API ftl_status_t ftl_ingest_disconnect(ftl_handle_t* ftl_handle);
        [DllImport(Libraries.Ftl, CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5393:安全でない DllImportSearchPath 値を使用しない", Justification = "<保留中>")]
        internal static extern Status ftl_ingest_disconnect(
            [In] IntPtr ftl_handle
        );

        //FTL_API ftl_status_t ftl_ingest_destroy(ftl_handle_t* ftl_handle);
        [DllImport(Libraries.Ftl, CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5393:安全でない DllImportSearchPath 値を使用しない", Justification = "<保留中>")]
        internal static extern Status ftl_ingest_destroy(
            [In] IntPtr ftl_handle
        );

        //FTL_API char* ftl_status_code_to_string(ftl_status_t status);

        //FTL_API ftl_status_t ftl_find_closest_available_ingest(const char* ingestHosts[], int ingestsCount, char* bestIngestHostComputed);

        //FTL_API ftl_status_t ftl_get_video_stats(ftl_handle_t* handle, uint64_t* frames_sent, uint64_t* nacks_received, uint64_t* rtt_recorded, uint64_t* frames_dropped, float* queue_fullness);

        /*
    FTL_API ftl_status_t ftl_adaptive_bitrate_thread(
        ftl_handle_t* ftl_handle,
        void* context,
        int(* change_bitrate_callback)(void*, uint64_t),
        uint64_t initial_encoding_bitrate,
        uint64_t min_encoding_bitrate,
        uint64_t max_encoding_bitrate
    );
    */
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct FtlStatusMsg
    {
        public StatusTypes status;
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct FtlStatusLogMsg
    {
        public StatusTypes status;
        public int padding;
        public LogSeverity log_level;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string msg;
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct FtlStatusEventMsg
    {
        public StatusTypes status;
        public int padding;
        public StatusEventType type;
        public StatusEventReasons reason;
        public Status error_code;
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct FtlPacketStatsMsg
    {
        public StatusTypes status;
        public int padding;
        public long period; //period of time in ms the stats were collected over
        public long sent;
        public long nack_reqs;
        public long lost;
        public long recovered;
        public long late;
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct FtlPacketStatsInstantMsg
    {
        public StatusTypes status;
        public int padding;
        public long period; //period of time in ms the stats were collected over
        public int min_rtt;
        public int max_rtt;
        public int avg_rtt;
        public int min_xmit_delay;
        public int max_xmit_delay;
        public int avg_xmit_delay;
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct FtlVideoFrameStatsMsg
    {
        public StatusTypes status;
        public int padding;
        public long period; //period of time in ms the stats were collected over
        public long frames_queued;
        public long frames_sent;
        public long bytes_queued;
        public long bytes_sent;
        public long bw_throttling_count;
        public int queue_fullness;
        public int max_frame_size;
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct FtlBitrateChangedMsg
    {
        public StatusTypes status;
        public int padding;
        public BitrateChangedType bitrate_changed_type;
        public BitrateChangedReason bitrate_changed_reason;
        public long current_encoding_bitrate;
        public long previous_encoding_bitrate;
        public float nacks_to_frames_ratio;
        public float avg_rtt;
        public long avg_frames_dropped;
        public float queue_fullness;
    };

    /*
    typedef struct {
      int pkts_sent;
            int nack_requests;
            int lost_pkts;
            int starting_rtt;
            int ending_rtt;
            int bytes_sent;
            int duration_ms;
            int peak_kbps;
        }
        speed_test_t;
    */

}

#pragma warning restore CA1051 // 参照可能なインスタンス フィールドを宣言しません
#pragma warning restore CA1815 // equals および operator equals を値型でオーバーライドします
#pragma warning restore CA1707 // 識別子はアンダースコアを含むことはできません
