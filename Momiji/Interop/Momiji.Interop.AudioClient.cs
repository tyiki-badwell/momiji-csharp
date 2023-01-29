using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Momiji.Interop.Wave;

namespace Momiji.Interop.AudioClient;

internal static class NativeMethods
{
    public enum AUDCLNT_SHAREMODE
    {
        SHARED = 0,
        EXCLUSIVE = 1,
    }

    [Flags]
    public enum AUDCLNT_STREAMFLAGS : uint
    {
        SESSIONFLAGS_EXPIREWHENUNOWNED = 0x10000000,
        SESSIONFLAGS_DISPLAY_HIDE = 0x20000000,
        SESSIONFLAGS_DISPLAY_HIDEWHENEXPIRED = 0x40000000,

        STREAMFLAGS_CROSSPROCESS = 0x00010000,
        STREAMFLAGS_LOOPBACK = 0x00020000,
        STREAMFLAGS_EVENTCALLBACK = 0x00040000,
        STREAMFLAGS_NOPERSIST = 0x00080000,
        STREAMFLAGS_RATEADJUST = 0x00100000,
        STREAMFLAGS_AUTOCONVERTPCM = 0x80000000,
        STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000
    }

    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComImport]
    internal interface IAudioClient
    {
        [PreserveSig]
        int Initialize(
            AUDCLNT_SHAREMODE ShareMode,
            AUDCLNT_STREAMFLAGS StreamFlags,
            long hnsBufferDuration,
            long hnsPeriodicity,
            ref WaveFormatExtensible pFormat,
            [Optional] ref Guid AudioSessionGuid
        );

        [PreserveSig]
        int GetBufferSize(
            [Out] out uint pNumBufferFrames
        );

        [PreserveSig]
        int GetStreamLatency(
            [Out] out long phnsLatency
        );

        [PreserveSig]
        int GetCurrentPadding(
            [Out] out uint pNumPaddingFrames
        );

        [PreserveSig]
        int IsFormatSupported(
            AUDCLNT_SHAREMODE ShareMode,
            ref WaveFormatExtensible pFormat,
            [Out] out nint ppClosestMatch
        );

        [PreserveSig]
        int GetMixFormat(
            [Out] out nint ppDeviceFormat
        );

        [PreserveSig]
        int GetDevicePeriod(
            [Out] out long phnsDefaultDevicePeriod,
            [Out] out long phnsMinimumDevicePeriod
        );

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(
            SafeWaitHandle eventHandle
        );

        [PreserveSig]
        int GetService(
            ref Guid riid,
            [Out][MarshalAs(UnmanagedType.IUnknown)] out object? ppv
        );
    }

    public enum AUDIO_STREAM_CATEGORY
    {
        Other = 0,
        ForegroundOnlyMedia = 1,
        BackgroundCapableMedia = 2,
        Communications = 3,
        Alerts = 4,
        SoundEffects = 5,
        GameEffects = 6,
        GameMedia = 7,
        GameChat = 8,
        Speech = 9,
        Movie = 10,
        Media = 11,
        FarFieldSpeech = 12,
        UniformSpeech = 13,
        VoiceTyping = 14
    }

    [Flags]
    public enum AUDCLNT_STREAMOPTIONS
    {
        NONE = 0,
        RAW = 0x1,
        MATCH_FORMAT = 0x2,
        AMBISONICS = 0x4
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioClientProperties
    {
        public uint cbSize;
        [MarshalAs(UnmanagedType.Bool)] public bool bIsOffload;
        public AUDIO_STREAM_CATEGORY eCategory;
        public AUDCLNT_STREAMOPTIONS Options;
    }

    [Guid("726778CD-F60A-4EDA-82DE-E47610CD78AA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComImport]
    internal interface IAudioClient2
    {
        [PreserveSig]
        int Initialize(
            AUDCLNT_SHAREMODE ShareMode,
            AUDCLNT_STREAMFLAGS StreamFlags,
            long hnsBufferDuration,
            long hnsPeriodicity,
            ref WaveFormatExtensible pFormat,
            [Optional] ref Guid AudioSessionGuid
        );

        [PreserveSig]
        int GetBufferSize(
            [Out] out uint pNumBufferFrames
        );

        [PreserveSig]
        int GetStreamLatency(
            [Out] out long phnsLatency
        );

        [PreserveSig]
        int GetCurrentPadding(
            [Out] out uint pNumPaddingFrames
        );

        [PreserveSig]
        int IsFormatSupported(
            AUDCLNT_SHAREMODE ShareMode,
            ref WaveFormatExtensible pFormat,
            [Out] out nint ppClosestMatch
        );

        [PreserveSig]
        int GetMixFormat(
            [Out] out nint ppDeviceFormat
        );

        [PreserveSig]
        int GetDevicePeriod(
            [Out] out long phnsDefaultDevicePeriod,
            [Out] out long phnsMinimumDevicePeriod
        );

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(
            SafeWaitHandle eventHandle
        );

        [PreserveSig]
        int GetService(
            ref Guid riid,
            [Out][MarshalAs(UnmanagedType.IUnknown)] out object? ppv
        );

        //---------------------------------------------------------
        [PreserveSig]
        int IsOffloadCapable(
            AUDIO_STREAM_CATEGORY Category,
            [Out][MarshalAs(UnmanagedType.Bool)] out bool pbOffloadCapable
        );

        [PreserveSig]
        int SetClientProperties(
            ref AudioClientProperties pProperties
        );

        [PreserveSig]
        int GetBufferSizeLimits(
            ref WaveFormatExtensible pFormat,
            [MarshalAs(UnmanagedType.Bool)] bool bEventDriven,
            [Out] out long phnsMinBufferDuration,
            [Out] out long phnsMaxBufferDuration
        );
    }

    [Guid("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComImport]
    internal interface IAudioClient3
    {
        [PreserveSig]
        int Initialize(
            AUDCLNT_SHAREMODE ShareMode,
            AUDCLNT_STREAMFLAGS StreamFlags,
            long hnsBufferDuration,
            long hnsPeriodicity,
            ref WaveFormatExtensible pFormat,
            [Optional] ref Guid AudioSessionGuid
        );

        [PreserveSig]
        int GetBufferSize(
            [Out] out uint pNumBufferFrames
        );

        [PreserveSig]
        int GetStreamLatency(
            [Out] out long phnsLatency
        );

        [PreserveSig]
        int GetCurrentPadding(
            [Out] out uint pNumPaddingFrames
        );

        [PreserveSig]
        int IsFormatSupported(
            AUDCLNT_SHAREMODE ShareMode,
            ref WaveFormatExtensible pFormat,
            [Out] out nint ppClosestMatch
        );

        [PreserveSig]
        int GetMixFormat(
            [Out] out nint ppDeviceFormat
        );

        [PreserveSig]
        int GetDevicePeriod(
            [Out] out long phnsDefaultDevicePeriod,
            [Out] out long phnsMinimumDevicePeriod
        );

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(
            SafeWaitHandle eventHandle
        );

        [PreserveSig]
        int GetService(
            ref Guid riid,
            [Out][MarshalAs(UnmanagedType.IUnknown)] out object? ppv
        );

        //---------------------------------------------------------
        [PreserveSig]
        int IsOffloadCapable(
            AUDIO_STREAM_CATEGORY Category,
            [Out][MarshalAs(UnmanagedType.Bool)] out bool pbOffloadCapable
        );

        [PreserveSig]
        int SetClientProperties(
            ref AudioClientProperties pProperties
        );

        [PreserveSig]
        int GetBufferSizeLimits(
            ref WaveFormatExtensible pFormat,
            [MarshalAs(UnmanagedType.Bool)] bool bEventDriven,
            [Out] out long phnsMinBufferDuration,
            [Out] out long phnsMaxBufferDuration
        );

        //---------------------------------------------------------
        [PreserveSig]
        int GetSharedModeEnginePeriod(
            ref WaveFormatExtensible pFormat,
            [Out] out uint pDefaultPeriodInFrames,
            [Out] out uint pFundamentalPeriodInFrames,
            [Out] out uint pMinPeriodInFrames,
            [Out] out uint pMaxPeriodInFrames
        );

        [PreserveSig]
        int GetCurrentSharedModeEnginePeriod(
            [Out] out nint ppFormat,
            [Out] out uint pCurrentPeriodInFrames
        );

        [PreserveSig]
        int InitializeSharedAudioStream(
            AUDCLNT_STREAMFLAGS StreamFlags,
            uint PeriodInFrames,
            ref WaveFormatExtensible pFormat,
            [Optional] ref Guid AudioSessionGuid
        );
    }

    [Flags]
    public enum AUDCLNT_BUFFERFLAGS
    {
        NONE = 0,
        DATA_DISCONTINUITY = 0x1,
        SILENT = 0x2,
        TIMESTAMP_ERROR = 0x4
    }

    [Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComImport]
    internal interface IAudioRenderClient
    {
        [PreserveSig]
        int GetBuffer(
            uint NumFramesRequested,
            [Out] out nint ppData
        );

        [PreserveSig]
        int ReleaseBuffer(
            uint NumFramesWritten,
            AUDCLNT_BUFFERFLAGS dwFlags
        );

        [PreserveSig]
        int GetBufferSizeLimits(
            ref WaveFormatExtensible pFormat,
            bool bEventDriven,
            [Out] out long phnsMinBufferDuration,
            [Out] out long phnsMaxBufferDuration
        );
    }

}

