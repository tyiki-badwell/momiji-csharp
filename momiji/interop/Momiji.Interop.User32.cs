using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Momiji.Interop.User32;

internal static class Libraries
{
    public const string User32 = "user32.dll";
}

internal sealed class HDesktop : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
{
    public HDesktop() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseDesktop(handle);
    }
}

internal static class NativeMethods
{
    public enum DF : uint
    {
        NONE = 0,
        ALLOWOTHERACCOUNTHOOK = 0x0001
    }

    [Flags]
    public enum ACCESS_MASK : uint
    {
        DELETE = 0x00010000,
        READ_CONTROL = 0x00020000,
        WRITE_DAC = 0x00040000,
        WRITE_OWNER = 0x00080000,
        SYNCHRONIZE = 0x00100000,

        STANDARD_RIGHTS_REQUIRED = DELETE | READ_CONTROL | WRITE_DAC | WRITE_OWNER,

        STANDARD_RIGHTS_READ = READ_CONTROL,
        STANDARD_RIGHTS_WRITE = READ_CONTROL,
        STANDARD_RIGHTS_EXECUTE = READ_CONTROL,

        STANDARD_RIGHTS_ALL = DELETE | READ_CONTROL | WRITE_DAC | WRITE_OWNER | SYNCHRONIZE,

        SPECIFIC_RIGHTS_ALL = 0x0000FFFF,

        ACCESS_SYSTEM_SECURITY = 0x01000000,

        MAXIMUM_ALLOWED = 0x02000000,

        GENERIC_READ = 0x80000000,
        GENERIC_WRITE = 0x40000000,
        GENERIC_EXECUTE = 0x20000000,
        GENERIC_ALL = 0x10000000,

        DESKTOP_READOBJECTS = 0x00000001,
        DESKTOP_CREATEWINDOW = 0x00000002,
        DESKTOP_CREATEMENU = 0x00000004,
        DESKTOP_HOOKCONTROL = 0x00000008,
        DESKTOP_JOURNALRECORD = 0x00000010,
        DESKTOP_JOURNALPLAYBACK = 0x00000020,
        DESKTOP_ENUMERATE = 0x00000040,
        DESKTOP_WRITEOBJECTS = 0x00000080,
        DESKTOP_SWITCHDESKTOP = 0x00000100,

        WINSTA_ENUMDESKTOPS = 0x00000001,
        WINSTA_READATTRIBUTES = 0x00000002,
        WINSTA_ACCESSCLIPBOARD = 0x00000004,
        WINSTA_CREATEDESKTOP = 0x00000008,
        WINSTA_WRITEATTRIBUTES = 0x00000010,
        WINSTA_ACCESSGLOBALATOMS = 0x00000020,
        WINSTA_EXITWINDOWS = 0x00000040,
        WINSTA_ENUMERATE = 0x00000100,
        WINSTA_READSCREEN = 0x00000200,

        WINSTA_ALL_ACCESS = 0x0000037F
    }

    internal struct SECURITY_ATTRIBUTES
    {
        public uint nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HDesktop CreateDesktopW(
        [In] string lpszDesktop,
        [In] IntPtr lpszDevice, /* NULL */
        [In] IntPtr pDevmode, /* NULL */
        [In] DF dwFlags,
        [In] ACCESS_MASK dwDesiredAccess,
        [In] IntPtr lpsa
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseDesktop(IntPtr hDesktop);


    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetThreadDesktop(this HDesktop hDesktop);





    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASS
    {
        [Flags]
        public enum CS : uint
        {
            NONE    = 0,
            VREDRAW = 0x00000001,
            HREDRAW = 0x00000002,
            DBLCLKS = 0x00000008,
            OWNDC = 0x00000020,
            CLASSDC = 0x00000040,
            PARENTDC = 0x00000080,
            NOCLOSE = 0x00000200,
            SAVEBITS = 0x00000800,
            BYTEALIGNCLIENT = 0x00001000,
            BYTEALIGNWINDOW = 0x00002000,
            GLOBALCLASS = 0x00004000,
            IME = 0x00010000,
            DROPSHADOW = 0x00020000
        }

        public CS style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi, SetLastError = false)]
    public delegate IntPtr WNDPROC(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern ushort RegisterClassW(
        [In] ref WNDCLASS lpWndClass
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClassW(
        [In] IntPtr lpClassName,
        [In] IntPtr hInstance
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsGUIThread(
        [In] bool bConvert
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr DefWindowProcA(
        [In] HandleRef hWnd,
        [In] uint msg,
        [In] IntPtr wParam,
        [In] IntPtr lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr DefWindowProcW(
        [In] HandleRef hWnd,
        [In] uint msg,
        [In] IntPtr wParam,
        [In] IntPtr lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr CreateWindowExW(
        [In] int dwExStyle,
        [In] IntPtr lpszClassName,
        [In] IntPtr lpszWindowName,
        [In] int style,
        [In] int x,
        [In] int y,
        [In] int width,
        [In] int height,
        [In] IntPtr hwndParent,
        [In] IntPtr hMenu,
        [In] IntPtr hInst,
        [In] IntPtr pvParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(
        [In] HandleRef hwnd
    );

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public int time;
        public int pt_x;
        public int pt_y;
    }

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowUnicode(
        [In] HandleRef hwnd
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint MsgWaitForMultipleObjects/*Ex*/(
        [In] uint nCount,
        [In] IntPtr pHandles,
        [In][MarshalAs(UnmanagedType.Bool)] bool fWaitAll,
        [In] uint dwMilliseconds,
        [In] uint dwWakeMask/*,
        [In] uint dwFlags*/
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetMessageA(
        [In, Out] ref MSG msg,
        [In] IntPtr hwnd,
        [In] int nMsgFilterMin,
        [In] int nMsgFilterMax
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetMessageW(
        [In] ref MSG msg,
        [In] IntPtr hwnd,
        [In] int nMsgFilterMin,
        [In] int nMsgFilterMax
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessageW(
        [In, Out] ref MSG msg,
        [In] IntPtr hwnd,
        [In] int nMsgFilterMin,
        [In] int nMsgFilterMax,
        [In] int wRemoveMsg
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(
        [In, Out] ref MSG msg
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr DispatchMessageA(
        [In] ref MSG msg
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr DispatchMessageW(
        [In] ref MSG msg
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SendNotifyMessageA(
        [In] HandleRef hWnd,
        [In] int nMsg,
        [In] IntPtr wParam,
        [In] IntPtr lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SendNotifyMessageW(
        [In] HandleRef hWnd,
        [In] int nMsg,
        [In] IntPtr wParam,
        [In] IntPtr lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr SetWindowLongPtrA(
        [In] HandleRef hWnd,
        [In] int nIndex,
        [In] IntPtr dwNewLong
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr SetWindowLongPtrW(
        [In] HandleRef hWnd,
        [In] int nIndex,
        [In] IntPtr dwNewLong
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr SetWindowLongA(
        [In] HandleRef hWnd,
        [In] int nIndex,
        [In] IntPtr dwNewLong
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr SetWindowLongW(
        [In] HandleRef hWnd,
        [In] int nIndex,
        [In] IntPtr dwNewLong
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr CallWindowProcA(
        [In] IntPtr lpPrevWndFunc,
        [In] HandleRef hWnd,
        [In] uint Msg,
        [In] IntPtr wParam,
        [In] IntPtr lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr CallWindowProcW(
        [In] IntPtr lpPrevWndFunc,
        [In] HandleRef hWnd,
        [In] uint Msg,
        [In] IntPtr wParam,
        [In] IntPtr lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern void PostQuitMessage(
        [In] int nExitCode
    );


    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveWindow(
        [In] HandleRef hWnd,
        [In] int X,
        [In] int Y,
        [In] int nWidth,
        [In] int nHeight,
        [In][MarshalAs(UnmanagedType.Bool)] bool bRepaint
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(
        [In] HandleRef hWnd,
        [In] int nCmdShow
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(
        [In] HandleRef hWnd,
        [In] int nCmdShow
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int InSendMessageEx(
        [In] IntPtr lpReserved
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReplyMessage(
        [In] IntPtr lResult
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern bool PrintWindow(
        [In] HandleRef hWnd,
        [In] HandleRef hDC,
        [In] int flags
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr GetDC(
        [In] HandleRef hWnd
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int ReleaseDC(
        [In] HandleRef hWnd,
        [In] HandleRef hDC
    );

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public long left;
        public long top;
        public long right;
        public long bottom;
    };

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(
        [In] HandleRef hWnd,
        [In] ref RECT lpRect
    );
}
