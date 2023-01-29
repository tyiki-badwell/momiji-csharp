using System.Runtime.InteropServices;

namespace Momiji.Interop.Opus;

public enum Bandwidth : int
{
    /// <summary>
    /// Up to 4Khz
    /// </summary>
    Narrowband = 1101,
    /// <summary>
    /// Up to 6Khz
    /// </summary>
    Mediumband = 1102,
    /// <summary>
    /// Up to 8Khz
    /// </summary>
    Wideband = 1103,
    /// <summary>
    /// Up to 12Khz
    /// </summary>
    SuperWideband = 1104,
    /// <summary>
    /// Up to 20Khz (High Definition)
    /// </summary>
    Fullband = 1105
}
public enum Channels : int
{
    /// <summary>
    /// 1 Channel
    /// </summary>
    Mono = 1,
    /// <summary>
    /// 2 Channels
    /// </summary>
    Stereo = 2
}

public enum Complexity : int
{
    Complexity0 = 0,
    Complexity1 = 1,
    Complexity2 = 2,
    Complexity3 = 3,
    Complexity4 = 4,
    Complexity5 = 5,
    Complexity6 = 6,
    Complexity7 = 7,
    Complexity8 = 8,
    Complexity9 = 9,
    Complexity10 = 10
}


/// <summary>
/// Using a duration of less than 10 ms will prevent the encoder from using the LPC or hybrid modes. 
/// </summary>
public enum Delay
{
    /// <summary>
    /// 2.5ms
    /// </summary>
    Delay2dot5ms = 5,
    /// <summary>
    /// 5ms
    /// </summary>
    Delay5ms = 10,
    /// <summary>
    /// 10ms
    /// </summary>
    Delay10ms = 20,
    /// <summary>
    /// 20ms
    /// </summary>
    Delay20ms = 40,
    /// <summary>
    /// 40ms
    /// </summary>
    Delay40ms = 80,
    /// <summary>
    /// 60ms
    /// </summary>
    Delay60ms = 120
}

public enum ForceChannels : int
{
    NoForce = -1000,
    Mono = 1,
    Stereo = 2
}

public enum OpusApplicationType : int
{
    /// <summary>
    /// Gives best quality at a given bitrate for voice signals.
    /// It enhances the input signal by high-pass filtering and emphasizing formants and harmonics.
    /// Optionally it includes in-band forward error correction to protect against packet loss.
    /// Use this mode for typical VoIP applications.
    /// Because of the enhancement, even at high bitrates the output may sound different from the input.
    /// </summary>
    Voip = 2048,
    /// <summary>
    /// Gives best quality at a given bitrate for most non-voice signals like music.
    /// Use this mode for music and mixed (music/voice) content, broadcast, and applications requiring less than 15 ms of coding delay.
    /// </summary>
    Audio = 2049,
    /// <summary>
    /// Configures low-delay mode that disables the speech-optimized mode in exchange for slightly reduced delay.
    /// </summary>
    RestrictedLowDelay = 2051
}

public enum OpusCtlGetRequest : int
{
    Application = 4001,
    Bitrate = 4003,
    MaxBandwidth = 4005,
    VBR = 4007,
    Bandwidth = 4009,
    Complexity = 4011,
    InbandFec = 4013,
    PacketLossPercentage = 4015,
    Dtx = 4017,
    VBRConstraint = 4021,
    ForceChannels = 4023,
    Signal = 4025,
    LookAhead = 4027,
    SampleRate = 4029,
    FinalRange = 4031,
    Pitch = 4033,
    Gain = 4035,
    LsbDepth = 4037
}

public enum OpusCtlSetRequest : int
{
    Application = 4000,
    Bitrate = 4002,
    MaxBandwidth = 4004,
    VBR = 4006,
    Bandwidth = 4008,
    Complexity = 4010,
    InbandFec = 4012,
    PacketLossPercentage = 4014,
    Dtx = 4016,
    VBRConstraint = 4020,
    ForceChannels = 4022,
    Signal = 4024,
    Gain = 4034,
    LsbDepth = 4036
}
public enum OpusStatusCode : int
{
    OK = 0,
    BadArguments = -1,
    BufferTooSmall = -2,
    InternalError = -3,
    InvalidPacket = -4,
    Unimplemented = -5,
    InvalidState = -6,
    AllocFail = -7
}

public enum SamplingRate : int
{
    Sampling08000 = 8000,
    Sampling12000 = 12000,
    Sampling16000 = 16000,
    Sampling24000 = 24000,
    Sampling48000 = 48000
}

public enum SignalHint : int
{
    /// <summary>
    /// (default) 
    /// </summary>
    Auto = -1000,
    /// <summary>
    /// Bias thresholds towards choosing LPC or Hybrid modes
    /// </summary>
    Voice = 3001,
    /// <summary>
    /// Bias thresholds towards choosing MDCT modes. 
    /// </summary>
    Music = 3002
}

internal static class Libraries
{
    public const string Opus = "opus.dll";
}

internal sealed class Encoder : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
{
    public Encoder() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        //Trace.WriteLine("opus_encoder_destroy");
        NativeMethods.opus_encoder_destroy(handle);
        return true;
    }
}

internal static class NativeMethods
{
    [DllImport(Libraries.Opus, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
    internal static extern Encoder
        opus_encoder_create(
            [In] SamplingRate Fs,
            [In] Channels channels,
            [In] OpusApplicationType application,
            [Out] out OpusStatusCode error
        );

    [DllImport(Libraries.Opus, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
    internal static extern void opus_encoder_destroy(
        [In] nint st
    );

    [DllImport(Libraries.Opus, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
    internal static extern int opus_encoder_get_size(
        [In] Channels channels
    );

    [DllImport(Libraries.Opus, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
    internal static extern OpusStatusCode opus_encoder_init(
        [In] this Encoder st,
        [In] SamplingRate Fs,
        [In] Channels channels,
        [In] OpusApplicationType application
    );

    [DllImport(Libraries.Opus, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
    internal static extern int opus_encode(
        [In] this Encoder st,
        [In] nint pcm,
        [In] int frame_size,
        [In] nint data,
        [In] int max_data_bytes
    );

    [DllImport(Libraries.Opus, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
    internal static extern int opus_encode_float(
        [In] this Encoder st,
        [In] nint pcm,
        [In] int frame_size,
        [In] nint data,
        [In] int max_data_bytes
    );

    [DllImport(Libraries.Opus, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
    internal static extern int opus_encoder_ctl(
        [In] this Encoder st,
        [In] OpusCtlSetRequest request,
        [In] int value
    );

    [DllImport(Libraries.Opus, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
    internal static extern int opus_encoder_ctl(
        [In] this Encoder st,
        [In] OpusCtlGetRequest request,
        [In, Out] ref int value
    );

    [DllImport(Libraries.Opus, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
    internal static extern int opus_packet_get_bandwidth(
        [In] nint data
    );

    [DllImport(Libraries.Opus, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
    internal static extern int opus_packet_get_nb_channels(
        [In] nint data
    );

    [DllImport(Libraries.Opus, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
    [return: MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(LPStrNonFree))]
    internal static extern string opus_strerror(
        [In] int error
    );

    [DllImport(Libraries.Opus, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies)]
    [return: MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(LPStrNonFree))]
    internal static extern string opus_get_version_string();
}

//opus_strerrorとopus_get_version_stringは、今のところ即値を返す実装になっているため、戻り値の文字列を開放しないマーシャラーを用意する
internal class LPStrNonFree : ICustomMarshaler
{
    private static readonly LPStrNonFree marshaler = new();
    public static ICustomMarshaler GetInstance(string _)
    {
        return marshaler;
    }

    public void CleanUpManagedData(object ManagedObj)
    {
        throw new NotImplementedException();
    }

    public void CleanUpNativeData(nint pNativeData)
    {
        // NOP
    }

    public int GetNativeDataSize()
    {
        throw new NotImplementedException();
    }

    public nint MarshalManagedToNative(object ManagedObj)
    {
        throw new NotImplementedException();
    }

    public object MarshalNativeToManaged(nint pNativeData)
    {
        var result = Marshal.PtrToStringAnsi(pNativeData);
        return (result == default) ? "": result;
    }
}
