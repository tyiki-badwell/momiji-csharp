using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Momiji.Interop.Kernel32;

internal static class Libraries
{
    public const string Kernel32 = "kernel32.dll";
}

internal static class NativeMethods
{
    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern bool SetDllDirectory(
        [In] string lpPathName
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr GetModuleHandle(
        [In] string? lpModuleName
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern WaitableTimer CreateWaitableTimer(
        [In, Optional] IntPtr lpTimerAttributes,
        [In][MarshalAs(UnmanagedType.Bool)] bool bManualReset,
        [In, Optional] IntPtr lpTimerName
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern WaitableTimer CreateWaitableTimerEx(
        [In, Optional] IntPtr lpTimerAttributes,
        [In, Optional] IntPtr lpTimerName,
        [In] WaitableTimer.FLAGS dwFlags,
        [In] WaitableTimer.ACCESSES dwDesiredAccess
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWaitableTimerEx(
        [In] this WaitableTimer hTimer,
        [In] ref long lpDueTime,
        [In] int lPeriod,
        [In, Optional] IntPtr pfnCompletionRoutine,
        [In, Optional] IntPtr lpArgToCompletionRoutine,
        [In, Optional] IntPtr WakeContext,
        [In] uint TolerableDelay
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int WaitForSingleObject(
        [In] this WaitableTimer hTimer,
        [In] uint dwMilliseconds
    );

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(
        [In] IntPtr hObject
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
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
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
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern void GetStartupInfoW(ref STARTUPINFOW lpStartupInfo);

    [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static unsafe extern uint FormatMessageW(
        [In] uint dwFlags,
        [In] IntPtr lpSource,
        [In] int dwMessageId,
        [In] uint dwLanguageId,
        [In] char* pszText,
        [In] uint nSize,
        [In] IntPtr Arguments
    );

}

internal sealed class WaitableTimer : SafeHandleZeroOrMinusOneIsInvalid
{
    [Flags]
    public enum FLAGS : uint
    {
        MANUAL_RESET = 0x00000001,
        HIGH_RESOLUTION = 0x00000002,
    }

    [Flags]
    public enum ACCESSES : long
    {
        DELETE = 0x00010000L,
        READ_CONTROL = 0x00020000L,
        SYNCHRONIZE = 0x00100000L,
        WRITE_DAC = 0x00040000L,
        WRITE_OWNER = 0x00080000L,
        TIMER_ALL_ACCESS = 0x1F0003,
        TIMER_MODIFY_STATE = 0x0002,
        TIMER_QUERY_STATE = 0x0001,

    }

    public WaitableTimer() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        var result = NativeMethods.CloseHandle(handle);
        return result;
    }


}
