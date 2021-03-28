using System;
using System.Diagnostics;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Permissions;

namespace Momiji.Interop.Wave
{
#pragma warning disable CA1815 // equals および operator equals を値型でオーバーライドします
#pragma warning disable IDE1006 // 命名スタイル
    public enum MMRESULT : uint
    {
        NOERROR = 0,

        WAVERR_BASE = 32,

        MIDIERR_BASE = 64,

        TIMERR_BASE = 96,
        TIMERR_NOCANDO = (TIMERR_BASE + 1),      // request not completed
    }

    public static class DriverCallBack
    {
        public enum TYPE : ulong
        {
            TYPEMASK = 0x00070000L, // callback type mask
            NULL = 0x00000000L, // no callback 
            WINDOW = 0x00010000L,   // dwCallback is a HWND
            TASK = 0x00020000L, // dwCallback is a HTASK
            FUNCTION = 0x00030000L, // dwCallback is a FARPROC
            THREAD = TASK,          // thread ID replaces 16 bit task
            EVENT = 0x00050000L,    // dwCallback is an EVENT Handle

            WAVE_FORMAT_QUERY = 0x0001,
            WAVE_ALLOWSYNC = 0x0002,
            WAVE_MAPPED = 0x0004,
            WAVE_FORMAT_DIRECT = 0x0008,
            WAVE_FORMAT_DIRECT_QUERY = (WAVE_FORMAT_QUERY | WAVE_FORMAT_DIRECT),
            WAVE_MAPPED_DEFAULT_COMMUNICATION_DEVICE = 0x0010,
        };

        public enum MM_EXT_WINDOW_MESSAGE : uint
        {
            WOM_OPEN = 0x3BB,   // waveform output
            WOM_CLOSE = 0x3BC,
            WOM_DONE = 0x3BD,

            WIM_OPEN = 0x3BE,   // waveform input
            WIM_CLOSE = 0x3BF,
            WIM_DATA = 0x3C0,

            MIM_OPEN = 0x3C1,   // MIDI input 
            MIM_CLOSE = 0x3C2,
            MIM_DATA = 0x3C3,
            MIM_LONGDATA = 0x3C4,
            MIM_ERROR = 0x3C5,
            MIM_LONGERROR = 0x3C6,
            MIM_MOREDATA = 0x3CC,   // MIM_DONE w/ pending events 

            MOM_OPEN = 0x3C7,   // MIDI output 
            MOM_CLOSE = 0x3C8,
            MOM_DONE = 0x3C9,
            MOM_POSITIONCB = 0x3CA, // Callback for MEVT_POSITIONCB 
        };

        public delegate void Proc(
            IntPtr hdrvr,
            MM_EXT_WINDOW_MESSAGE uMsg,
            IntPtr dwUser,
            IntPtr dw1,
            IntPtr dw2
        );
    };

    // wave data block header
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public class WaveHeader
    {
        // flags for dwFlags field of WAVEHDR
        [Flags]
        public enum FLAG : uint
        {
            DONE = 0x00000001,  // done bit
            PREPARED = 0x00000002,  // set if this header has been prepared
            BEGINLOOP = 0x00000004, // loop start block
            ENDLOOP = 0x00000008,   // loop end block
            INQUEUE = 0x00000010,   // reserved for driver
        }

        public IntPtr data;            // pointer to locked data buffer
        public uint bufferLength;    // length of data buffer
        public uint bytesRecorded;   // used for input only
        public IntPtr user;            // for client's use
        public FLAG flags;         // assorted flags (see defines)
        public uint loops;           // loop control counter
        public IntPtr next;            // reserved for driver
        public IntPtr reserved;        // reserved for driver

        public override string ToString()
        {
            return "data[" + data + "] bufferLength[" + bufferLength + "] bytesRecorded[" + bytesRecorded + "] user[" + user + "] flags[" + flags.ToString("F") + "] loops[" + loops + "] next[" + next + "] reserved[" + reserved + "]";
        }
    }

    //defines for dwFormat field of WAVEINCAPS and WAVEOUTCAPS
    [Flags]
    public enum WAVE_FORMAT : uint
    {
        FORMAT_INVALID = 0x00000000,    // invalid format
        FORMAT_1M08 = 0x00000001,   // 11.025 kHz, Mono,   8-bit 
        FORMAT_1S08 = 0x00000002,   // 11.025 kHz, Stereo, 8-bit 
        FORMAT_1M16 = 0x00000004,   // 11.025 kHz, Mono,   16-bit
        FORMAT_1S16 = 0x00000008,   // 11.025 kHz, Stereo, 16-bit
        FORMAT_2M08 = 0x00000010,   // 22.05  kHz, Mono,   8-bit 
        FORMAT_2S08 = 0x00000020,   // 22.05  kHz, Stereo, 8-bit 
        FORMAT_2M16 = 0x00000040,   // 22.05  kHz, Mono,   16-bit
        FORMAT_2S16 = 0x00000080,   // 22.05  kHz, Stereo, 16-bit
        FORMAT_4M08 = 0x00000100,   // 44.1   kHz, Mono,   8-bit 
        FORMAT_4S08 = 0x00000200,   // 44.1   kHz, Stereo, 8-bit 
        FORMAT_4M16 = 0x00000400,   // 44.1   kHz, Mono,   16-bit
        FORMAT_4S16 = 0x00000800,   // 44.1   kHz, Stereo, 16-bit
        FORMAT_44M08 = 0x00000100,  // 44.1   kHz, Mono,   8-bit 
        FORMAT_44S08 = 0x00000200,  // 44.1   kHz, Stereo, 8-bit 
        FORMAT_44M16 = 0x00000400,  // 44.1   kHz, Mono,   16-bit
        FORMAT_44S16 = 0x00000800,  // 44.1   kHz, Stereo, 16-bit
        FORMAT_48M08 = 0x00001000,  // 48     kHz, Mono,   8-bit 
        FORMAT_48S08 = 0x00002000,  // 48     kHz, Stereo, 8-bit 
        FORMAT_48M16 = 0x00004000,  // 48     kHz, Mono,   16-bit
        FORMAT_48S16 = 0x00008000,  // 48     kHz, Stereo, 16-bit
        FORMAT_96M08 = 0x00010000,  // 96     kHz, Mono,   8-bit 
        FORMAT_96S08 = 0x00020000,  // 96     kHz, Stereo, 8-bit 
        FORMAT_96M16 = 0x00040000,  // 96     kHz, Mono,   16-bit
        FORMAT_96S16 = 0x00080000,  // 96     kHz, Stereo, 16-bit
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct WaveOutCapabilities
    {
        //flags for dwSupport field of WAVEOUTCAPS
        [Flags]
        public enum WAVECAPS : uint
        {
            PITCH = 0x0001, // supports pitch control
            PLAYBACKRATE = 0x0002,  // supports playback rate control
            VOLUME = 0x0004,    // supports volume control
            LRVOLUME = 0x0008,  // separate left-right volume control
            SYNC = 0x0010,
            SAMPLEACCURATE = 0x0020,
        }

        public ushort manufacturerID;
        public ushort productID;
        public uint driverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string productName;
        public WAVE_FORMAT formats;
        public ushort channels;
        public ushort reserved1;
        public WAVECAPS support;
        public Guid manufacturerGuid;      // for extensible MID mapping 
        public Guid productGuid;           // for extensible PID mapping
        public Guid nameGuid;              // for name lookup in registry

        public override string ToString()
        {
            return
                $"manufacturerID[{manufacturerID}] " +
                $"productID[{productID}] " +
                $"driverVersion[{driverVersion}] " +
                $"productName[{productName}] " +
                $"formats[{formats}] " +
                $"channels[{channels}] " +
                $"reserved1[{reserved1}] " +
                $"support[{support}] " +
                $"manufacturerGuid[{manufacturerGuid}] " +
                $"productGuid[{productGuid}] " +
                $"nameGuid[{nameGuid}] "
                ;
        }
    };

    // general extended waveform format structure
    // Use this for all NON PCM formats
    // (information common to all formats)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct WaveFormatEx
    {
        // flags for wFormatTag field of WAVEFORMAT
        public enum FORMAT : ushort
        {
            PCM = 0x0001,
            ADPCM = 0x0002,
            IEEE_FLOAT = 0x0003,

            EXTENSIBLE = 0xFFFE,
        }

        public FORMAT formatType;                  // format type
        public ushort channels;                    // number of channels (i.e. mono, stereo...)
        public uint samplesPerSecond;            // sample rate
        public uint averageBytesPerSecond;       // for buffer estimation
        public ushort blockAlign;                  // block size of data
        public ushort bitsPerSample;               // Number of bits per sample of mono data
        public ushort size;                        // The count in bytes of the size of extra information (after cbSize)

        public override string ToString()
        {
            return "formatType[" + formatType.ToString("F") + "] channels[" + channels + "] samplesPerSecond[" + samplesPerSecond + "] averageBytesPerSecond[" + averageBytesPerSecond + "] blockAlign[" + blockAlign + "] bitsPerSample[" + bitsPerSample + "] size[" + size + "]";
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct WaveFormat
    {
        public WaveFormatEx.FORMAT formatType;                // format type
        public ushort channels;                // number of channels (i.e. mono, stereo...)
        public uint samplesPerSecond;        // sample rate
        public uint averageBytesPerSecond;   // for buffer estimation
        public ushort blockAlign;              // block size of data

        public override string ToString()
        {
            return
                "formatType[" + formatType.ToString("F") + "] channels[" + channels + "] samplesPerSecond[" + samplesPerSecond + "] averageBytesPerSecond[" + averageBytesPerSecond + "] blockAlign[" + blockAlign + "]";
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct PcmWaveFormat
    {
        public WaveFormat wf;
        public ushort bitsPerSample;               // Number of bits per sample of mono data

        public override string ToString()
        {
            return
                "wf[" + wf.ToString() + "] bitsPerSample[" + bitsPerSample + "]";
        }
    }

    //
    //  New wave format development should be based on the
    //  WAVEFORMATEXTENSIBLE structure. WAVEFORMATEXTENSIBLE allows you to
    //  avoid having to register a new format tag with Microsoft. Simply
    //  define a new GUID value for the WAVEFORMATEXTENSIBLE.SubFormat field
    //  and use WAVE_FORMAT_EXTENSIBLE in the
    //  WAVEFORMATEXTENSIBLE.Format.wFormatTag field.
    //
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct WaveFormatExtensiblePart
    {
        [Flags]
        public enum SPEAKER : uint
        {
            FRONT_LEFT = 0x00000001,
            FRONT_RIGHT = 0x00000002,
            FRONT_CENTER = 0x00000004,
            LOW_FREQUENCY = 0x00000008,
            BACK_LEFT = 0x00000010,
            BACK_RIGHT = 0x00000020,
            FRONT_LEFT_OF_CENTER = 0x00000040,
            FRONT_RIGHT_OF_CENTER = 0x00000080,
            BACK_CENTER = 0x00000100,
            SIDE_LEFT = 0x00000200,
            SIDE_RIGHT = 0x00000400,
            TOP_CENTER = 0x00000800,
            TOP_FRONT_LEFT = 0x00001000,
            TOP_FRONT_CENTER = 0x00002000,
            TOP_FRONT_RIGHT = 0x00004000,
            TOP_BACK_LEFT = 0x00008000,
            TOP_BACK_CENTER = 0x00010000,
            TOP_BACK_RIGHT = 0x00020000,
        //    RESERVED = 0x7FFC0000,  // Bit mask locations reserved for future use
            ALL = 0x80000000,   // Used to specify that any possible permutation of speaker configurations
        }

        public ushort validBitsPerSample;
        public SPEAKER channelMask;
        public Guid subFormat;

        public override string ToString()
        {
            return
                "validBitsPerSample[" + validBitsPerSample + "] channelMask[" + channelMask.ToString("F") + "] subFormat[" + subFormat + "]";
        }
    }


    //
    //  extended waveform format structure used for all non-PCM formats. this
    //  structure is common to all non-PCM formats.
    //
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct WaveFormatExtensible
    {
        public WaveFormatEx wfe;
        public WaveFormatExtensiblePart exp;

        public override string ToString()
        {
            return
                "wfe[" + wfe.ToString() + "] exp[" + exp.ToString() + "]";
        }

    }

    internal static class Libraries
    {
        public const string Winmm = "winmm.dll";
    }

    internal sealed class WaveOut : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        private WaveOut() : base(true)
        {
        }

        protected override bool ReleaseHandle()
        {
            MMRESULT mmResult = SafeNativeMethods.waveOutClose(handle);
            return (mmResult == MMRESULT.NOERROR);
        }

    }

    internal static class SafeNativeMethods
    {
        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern MMRESULT waveOutOpen(
            [Out]  out WaveOut phwo,
            [In]   uint uDeviceID,
            [In]   ref WaveFormatExtensible pwfx,
            //[In][MarshalAs(UnmanagedType.FunctionPtr)]DriverCallBack.Delegate dwCallback,
            [In]   IntPtr/*DriverCallBack.Delegate*/ dwCallback,
            [In]   IntPtr dwInstance,
            [In]   DriverCallBack.TYPE fdwOpen
        );

        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern MMRESULT waveOutClose(
            [In]   IntPtr hwo
        );

        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint waveOutGetNumDevs();

        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern MMRESULT waveOutGetDevCaps(
            [In]   uint uDeviceID,
            [In, Out]  ref WaveOutCapabilities pwoc,
            [In]   uint cbwoc
        );

        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern MMRESULT waveOutGetVolume(
            [In]   this WaveOut hwo,
            [Out]  out uint pdwVolume
        );

        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern MMRESULT waveOutSetVolume(
            [In]   this WaveOut hwo,
            [In]   uint pdwVolume
        );

        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern MMRESULT waveOutGetErrorText(
            [In]   MMRESULT mmrError,
            [Out]  System.Text.StringBuilder pszText,
            [In]   uint cchText
        );

        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern MMRESULT waveOutPrepareHeader(
            [In]   this WaveOut hwo,
            [In]   IntPtr pwh,
            [In]   uint cbwh
        );

        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern MMRESULT waveOutUnprepareHeader(
            [In]   this WaveOut hwo,
            [In]   IntPtr pwh,
            [In]   uint cbwh
        );

        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern MMRESULT waveOutWrite(
            [In]   this WaveOut hwo,
            [In]   IntPtr pwh,
            [In]   uint cbwh
        );

        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern MMRESULT waveOutPause(
            [In]   this WaveOut hwo
        );

        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern MMRESULT waveOutRestart(
            [In]   this WaveOut hwo
        );

        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern MMRESULT waveOutReset(
            [In]   this WaveOut hwo
        );

        [DllImport(Libraries.Winmm, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern MMRESULT waveOutBreakLoop(
            [In]   this WaveOut hwo
        );

        /*
        WINMMAPI MMRESULT WINAPI waveOutGetPosition( __in HWAVEOUT hwo, __inout_bcount(cbmmt) LPMMTIME pmmt, __in UINT cbmmt);
        WINMMAPI MMRESULT WINAPI waveOutGetPitch( __in HWAVEOUT hwo, __out LPDWORD pdwPitch);
        WINMMAPI MMRESULT WINAPI waveOutSetPitch( __in HWAVEOUT hwo, __in DWORD dwPitch);
        WINMMAPI MMRESULT WINAPI waveOutGetPlaybackRate( __in HWAVEOUT hwo, __out LPDWORD pdwRate);
        WINMMAPI MMRESULT WINAPI waveOutSetPlaybackRate( __in HWAVEOUT hwo, __in DWORD dwRate);
        WINMMAPI MMRESULT WINAPI waveOutGetID( __in HWAVEOUT hwo, __out LPUINT puDeviceID);
        WINMMAPI MMRESULT WINAPI waveOutMessage( __in_opt HWAVEOUT hwo, __in UINT uMsg, __in DWORD_PTR dw1, __in DWORD_PTR dw2);
        */
    }
#pragma warning restore IDE1006 // 命名スタイル
#pragma warning restore CA1815 // equals および operator equals を値型でオーバーライドします
}