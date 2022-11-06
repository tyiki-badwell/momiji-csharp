using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Momiji.Interop.User32;

internal static class Libraries
{
    public const string User32 = "user32.dll";
}

internal sealed class HWindowStation : SafeHandleZeroOrMinusOneIsInvalid
{
    public HWindowStation() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseWindowStation(handle);
    }
}

internal sealed class HDesktop : SafeHandleZeroOrMinusOneIsInvalid
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
    internal enum CWF : uint
    {
        NONE = 0,
        CREATE_ONLY = 0x0001
    }

    internal enum DF : uint
    {
        NONE = 0,
        ALLOWOTHERACCOUNTHOOK = 0x0001
    }

    [Flags]
    internal enum WINSTA_ACCESS_MASK : uint
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

    [Flags]
    internal enum DESKTOP_ACCESS_MASK : uint
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
    }

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern HWindowStation CreateWindowStationW(
        [In] string? lpwinsta,
        [In] CWF dwFlags,
        [In] WINSTA_ACCESS_MASK dwDesiredAccess, 
        [In] ref Advapi32.NativeMethods.SecurityAttributes lpsa
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseWindowStation(IntPtr hWinSta);

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessWindowStation(this HWindowStation hWinSta);

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern HWindowStation GetProcessWindowStation();

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HDesktop CreateDesktopW(
        [In] string? lpszDesktop,
        [In] IntPtr lpszDevice, /* NULL */
        [In] IntPtr pDevmode, /* NULL */
        [In] DF dwFlags,
        [In] DESKTOP_ACCESS_MASK dwDesiredAccess,
        [In] ref Advapi32.NativeMethods.SecurityAttributes lpsa
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseDesktop(IntPtr hDesktop);


    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetThreadDesktop(this HDesktop hDesktop);

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HDesktop GetThreadDesktop(int dwThreadId);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASS
    {
        [Flags]
        internal enum CS : uint
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
    internal delegate IntPtr WNDPROC(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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
        [In] IntPtr hWnd,
        [In] uint msg,
        [In] IntPtr wParam,
        [In] IntPtr lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr DefWindowProcW(
        [In] IntPtr hWnd,
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CREATESTRUCT
    {
        public IntPtr lpCreateParams;
        public IntPtr hInstance;
        public IntPtr hMenu;
        public IntPtr hwndParent;
        public int cy;
        public int cx;
        public int y;
        public int x;
        public long style;
        public IntPtr lpszName;
        public IntPtr lpszClass;
        public int dwExStyle;
    }

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(
        [In] IntPtr hwnd
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
        [In] IntPtr hwnd
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint GetQueueStatus(
        [In] uint flags
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint MsgWaitForMultipleObjectsEx(
        [In] uint nCount,
        [In] IntPtr pHandles,
        [In] uint dwMilliseconds,
        [In] uint dwWakeMask,
        [In] uint dwFlags
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
        [In] IntPtr hWnd,
        [In] int nMsg,
        [In] IntPtr wParam,
        [In] IntPtr lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SendNotifyMessageW(
        [In] IntPtr hWnd,
        [In] int nMsg,
        [In] IntPtr wParam,
        [In] IntPtr lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr SendMessageW(
        [In] IntPtr hWnd,
        [In] int nMsg,
        [In] IntPtr wParam,
        [In] IntPtr lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr SetWindowLongPtrA(
        [In] IntPtr hWnd,
        [In] int nIndex,
        [In] IntPtr dwNewLong
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr SetWindowLongPtrW(
        [In] IntPtr hWnd,
        [In] int nIndex,
        [In] IntPtr dwNewLong
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr SetWindowLongA(
        [In] IntPtr hWnd,
        [In] int nIndex,
        [In] IntPtr dwNewLong
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr SetWindowLongW(
        [In] IntPtr hWnd,
        [In] int nIndex,
        [In] IntPtr dwNewLong
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr CallWindowProcA(
        [In] IntPtr lpPrevWndFunc,
        [In] IntPtr hWnd,
        [In] uint Msg,
        [In] IntPtr wParam,
        [In] IntPtr lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr CallWindowProcW(
        [In] IntPtr lpPrevWndFunc,
        [In] IntPtr hWnd,
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
        [In] IntPtr hWnd,
        [In] int X,
        [In] int Y,
        [In] int nWidth,
        [In] int nHeight,
        [In][MarshalAs(UnmanagedType.Bool)] bool bRepaint
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
      [In] IntPtr hWnd,
      [In] IntPtr hWndInsertAfter,
      [In] int X,
      [In] int Y,
      [In] int cx,
      [In] int cy,
      [In] uint uFlags
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(
        [In] IntPtr hWnd,
        [In] int nCmdShow
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(
        [In] IntPtr hWnd,
        [In] int nCmdShow
    );

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public long x;
        public long y;
    }


    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPLACEMENT
    {
        [Flags]
        internal enum FLAG : uint
        {
            WPF_SETMINPOSITION = 0x0001,
            WPF_RESTORETOMAXIMIZED = 0x0002,
            WPF_ASYNCWINDOWPLACEMENT = 0x0004
        }
        public int length;
        public FLAG flags;
        public uint showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
        public RECT rcDevice;
    }

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(
        [In] IntPtr hWnd,
        [In, Out] ref WINDOWPLACEMENT lpwndpl
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
        [In] IntPtr hWnd,
        [In] IntPtr hDC,
        [In] int flags
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr GetDC(
        [In] IntPtr hWnd
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int ReleaseDC(
        [In] IntPtr hWnd,
        [In] IntPtr hDC
    );

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    };

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(
        [In] IntPtr hWnd,
        [In] ref RECT lpRect
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AdjustWindowRect(
        [In] ref RECT lpRect,
        [In] int dwStyle,
        [In][MarshalAs(UnmanagedType.Bool)] bool bMenu
    );

}
