using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Momiji.Interop.Kernel32;

internal static class Libraries
{
    public const string Kernel32 = "kernel32.dll";
}

internal static class NativeMethods
{
    [Flags]
    public enum BASE_SEARCH_PATH : uint
    {
        ENABLE_SAFE_SEARCHMODE = 0x00000001,
        DISABLE_SAFE_SEARCHMODE = 0x00010000,
        PERMANENT = 0x00008000
    }

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetSearchPathMode(
        [In] BASE_SEARCH_PATH Flags
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetDllDirectoryW(
        [In][MarshalAs(UnmanagedType.LPWStr)] string lpPathName
    );

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SYSTEM_INFO
    {
        public ushort wProcessorArchitecture;
        public ushort wReserved;
        public uint dwPageSize;
        public nint lpMinimumApplicationAddress;
        public nint lpMaximumApplicationAddress;
        public nint dwActiveProcessorMask;
        public uint dwNumberOfProcessors;
        public uint dwProcessorType;
        public uint dwAllocationGranularity;
        public ushort wProcessorLevel;
        public ushort wProcessorRevision;
    }

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern void GetNativeSystemInfo(
        [In] ref SYSTEM_INFO lpSystemInfo
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWow64Process2(
        [In] nint hProcess,
        [Out] out ushort pProcessMachine,
        [Out] out ushort pNativeMachine
    );

    [Flags]
    public enum DEP_SYSTEM_POLICY_TYPE : uint
    {
        AlwaysOff,
        AlwaysOn,
        OptIn,
        Optout
    }

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern DEP_SYSTEM_POLICY_TYPE GetSystemDEPPolicy();

    [Flags]
    public enum PROCESS_DEP : uint
    {
        NONE = 0,
        ENABLE = 1,
        DISABLE_ATL_THUNK_EMULATION = 2
    }

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessDEPPolicy(
        [In] nint hProcess,
        [Out] out PROCESS_DEP lpFlags,
        [Out][MarshalAs(UnmanagedType.Bool)] out bool lpPermanent
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDEPPolicy(
        [In] PROCESS_DEP dwFlags
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint GetModuleHandleW(
        [In][MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName
    );

    internal static class WAITABLE_TIMER
    {
        [Flags]
        public enum FLAGS : uint
        {
            MANUAL_RESET = 0x00000001,
            HIGH_RESOLUTION = 0x00000002,
        }

        [Flags]
        public enum ACCESS_MASK : uint
        {
            DELETE = 0x00010000,
            READ_CONTROL = 0x00020000,
            WRITE_DAC = 0x00040000,
            WRITE_OWNER = 0x00080000,
            SYNCHRONIZE = 0x00100000,

            QUERY_STATE = 0x0001,
            MODIFY_STATE = 0x0002,

            ALL_ACCESS = 0x1F0003
        }
    }

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern SafeWaitHandle CreateWaitableTimerW(
        [In, Optional] nint lpTimerAttributes,
        [In][MarshalAs(UnmanagedType.Bool)] bool bManualReset,
        [In, Optional] nint lpTimerName
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern SafeWaitHandle CreateWaitableTimerExW(
        [In, Optional] nint lpTimerAttributes,
        [In, Optional][MarshalAs(UnmanagedType.LPWStr)] string? lpTimerName,
        [In] WAITABLE_TIMER.FLAGS dwFlags,
        [In] WAITABLE_TIMER.ACCESS_MASK dwDesiredAccess
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWaitableTimerEx(
        [In] this SafeWaitHandle hTimer,
        [In] ref long lpDueTime,
        [In] int lPeriod,
        [In, Optional] nint pfnCompletionRoutine,
        [In, Optional] nint lpArgToCompletionRoutine,
        [In, Optional] nint WakeContext,
        [In] uint TolerableDelay
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int WaitForSingleObject(
        [In] this SafeWaitHandle hTimer,
        [In] uint dwMilliseconds
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(
        [In] nint hObject
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFOW
    {
        [Flags]
        public enum STARTF : uint
        {
            USESHOWWINDOW = 0x00000001,
            USESIZE = 0x00000002,
            USEPOSITION = 0x00000004,
            USECOUNTCHARS = 0x00000008,

            USEFILLATTRIBUTE = 0x00000010,
            RUNFULLSCREEN = 0x00000020,
            FORCEONFEEDBACK = 0x00000040,
            FORCEOFFFEEDBACK = 0x00000080,

            USESTDHANDLES = 0x00000100,
            USEHOTKEY = 0x00000200,
            TITLEISLINKNAME = 0x00000800,

            TITLEISAPPID = 0x00001000,
            PREVENTPINNING = 0x00002000,
            UNTRUSTEDSOURCE = 0x00008000,
        }

        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public STARTF dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern void GetStartupInfoW(ref STARTUPINFOW lpStartupInfo);

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static unsafe extern uint FormatMessageW(
        [In] uint dwFlags,
        [In] nint lpSource,
        [In] int dwMessageId,
        [In] uint dwLanguageId,
        [In] char* pszText,
        [In] uint nSize,
        [In] nint Arguments
    );

    public enum PROCESS_INFORMATION_CLASS : uint
    {
        ProcessMemoryPriority,
        ProcessMemoryExhaustionInfo,
        ProcessAppMemoryInfo,
        ProcessInPrivateInfo,
        ProcessPowerThrottling,
        ProcessReservedValue1,
        ProcessTelemetryCoverageInfo,
        ProcessProtectionLevelInfo,
        ProcessLeapSecondInfo,
        ProcessMachineTypeInfo,
        ProcessInformationClassMax
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class MEMORY_PRIORITY_INFORMATION
    {
        public enum MEMORY_PRIORITY : uint
        {
            UNKNOWN = 0,
            VERY_LOW,
            LOW,
            MEDIUM,
            BELOW_NORMAL,
            NORMAL
        };

        public MEMORY_PRIORITY MemoryPriority;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class PROCESS_POWER_THROTTLING_STATE
    {
        [Flags]
        public enum PROCESS_POWER_THROTTLING : uint
        {
            UNKNOWN = 0,
            EXECUTION_SPEED = 0x1,
            IGNORE_TIMER_RESOLUTION = 0x4
        };

        public uint Version;
        public PROCESS_POWER_THROTTLING ControlMask;
        public PROCESS_POWER_THROTTLING StateMask;
    }

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessInformation(
        [In] nint hProcess,
        [In] PROCESS_INFORMATION_CLASS ProcessInformationClass,
        [In] nint ProcessInformation,
        [In] int ProcessInformationSize
    );

}

