﻿using System.Runtime.InteropServices;
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
    internal enum WINSTA_ACCESS_MASK : int
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

        GENERIC_READ = unchecked((int)0x80000000),
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
    internal enum DESKTOP_ACCESS_MASK : int
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

        GENERIC_READ = unchecked((int)0x80000000),
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
    internal static extern bool CloseWindowStation(nint hWinSta);

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
        [In] nint lpszDevice, /* NULL */
        [In] nint pDevmode, /* NULL */
        [In] DF dwFlags,
        [In] DESKTOP_ACCESS_MASK dwDesiredAccess,
        [In] ref Advapi32.NativeMethods.SecurityAttributes lpsa
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseDesktop(nint hDesktop);


    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetThreadDesktop(this HDesktop hDesktop);

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HDesktop GetThreadDesktop(int dwThreadId);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WNDCLASSEX
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

        public int cbSize;
        public CS style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        public nint lpszClassName;
        public nint hIconSm;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi, SetLastError = false)]
    internal delegate nint WNDPROC(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern ushort RegisterClassExW(
        [In] ref WNDCLASSEX lpWndClass
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClassW(
        [In] nint lpClassName,
        [In] nint hInstance
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsGUIThread(
        [In] bool bConvert
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint DefWindowProcA(
        [In] nint hWnd,
        [In] uint msg,
        [In] nint wParam,
        [In] nint lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint DefWindowProcW(
        [In] nint hWnd,
        [In] uint msg,
        [In] nint wParam,
        [In] nint lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint CreateWindowExW(
        [In] int dwExStyle,
        [In] nint lpszClassName,
        [In] nint lpszWindowName,
        [In] int style,
        [In] int x,
        [In] int y,
        [In] int width,
        [In] int height,
        [In] nint hwndParent,
        [In] nint hMenu,
        [In] nint hInst,
        [In] nint pvParam
    );

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct CREATESTRUCT
    {
        public nint lpCreateParams;
        public nint hInstance;
        public nint hMenu;
        public nint hwndParent;
        public int cy;
        public int cx;
        public int y;
        public int x;
        public long style;
        public nint lpszName;
        public nint lpszClass;
        public int dwExStyle;
    }

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(
        [In] nint hwnd
    );

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct MSG
    {
        public nint hwnd;
        public int message;
        public nint wParam;
        public nint lParam;
        public int time;
        public POINT pt;

        public readonly override string ToString()
        {
            return
                $"hwnd[{hwnd:X}] message[{message:X}] wParam[{wParam:X}] lParam[{lParam:X}] time[{time}] pt[{pt}]";
        }
    }

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowUnicode(
        [In] nint hwnd
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
        [In] nint pHandles,
        [In] uint dwMilliseconds,
        [In] uint dwWakeMask,
        [In] uint dwFlags
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetMessageA(
        [In, Out] ref MSG msg,
        [In] nint hwnd,
        [In] int nMsgFilterMin,
        [In] int nMsgFilterMax
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetMessageW(
        [In] ref MSG msg,
        [In] nint hwnd,
        [In] int nMsgFilterMin,
        [In] int nMsgFilterMax
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessageW(
        [In, Out] ref MSG msg,
        [In] nint hwnd,
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
    internal static extern nint DispatchMessageA(
        [In] ref MSG msg
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint DispatchMessageW(
        [In] ref MSG msg
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SendNotifyMessageA(
        [In] nint hWnd,
        [In] uint nMsg,
        [In] nint wParam,
        [In] nint lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SendNotifyMessageW(
        [In] nint hWnd,
        [In] uint nMsg,
        [In] nint wParam,
        [In] nint lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint SendMessageW(
        [In] nint hWnd,
        [In] uint nMsg,
        [In] nint wParam,
        [In] nint lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint SetWindowLongPtrA(
        [In] nint hWnd,
        [In] int nIndex,
        [In] nint dwNewLong
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint SetWindowLongPtrW(
        [In] nint hWnd,
        [In] int nIndex,
        [In] nint dwNewLong
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint SetWindowLongA(
        [In] nint hWnd,
        [In] int nIndex,
        [In] nint dwNewLong
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint SetWindowLongW(
        [In] nint hWnd,
        [In] int nIndex,
        [In] nint dwNewLong
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint CallWindowProcA(
        [In] nint lpPrevWndFunc,
        [In] nint hWnd,
        [In] uint Msg,
        [In] nint wParam,
        [In] nint lParam
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint CallWindowProcW(
        [In] nint lpPrevWndFunc,
        [In] nint hWnd,
        [In] uint Msg,
        [In] nint wParam,
        [In] nint lParam
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
        [In] nint hWnd,
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
      [In] nint hWnd,
      [In] nint hWndInsertAfter,
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
        [In] nint hWnd,
        [In] int nCmdShow
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(
        [In] nint hWnd,
        [In] int nCmdShow
    );

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct POINT
    {
        public long x;
        public long y;
        public readonly override string ToString()
        {
            return
                $"x[{x}] y[{y}]";
        }
    }


    [StructLayout(LayoutKind.Sequential, Pack = 2)]
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
        public readonly override string ToString()
        {
            return
                $"formatType[{flags:F}] showCmd[{showCmd:X}] ptMinPosition[{ptMinPosition}] ptMaxPosition[{ptMaxPosition}] rcNormalPosition[{rcNormalPosition}] rcDevice[{rcDevice}]";
        }
    }

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(
        [In] nint hWnd,
        [In, Out] ref WINDOWPLACEMENT lpwndpl
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int InSendMessageEx(
        [In] nint lpReserved
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReplyMessage(
        [In] nint lResult
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern bool PrintWindow(
        [In] nint hWnd,
        [In] nint hDC,
        [In] int flags
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint GetDC(
        [In] nint hWnd
    );

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int ReleaseDC(
        [In] nint hWnd,
        [In] nint hDC
    );

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
        public readonly override string ToString()
        {
            return
                $"left[{left}] top[{top}] right[{right}] bottom[{bottom}]";
        }
    };

    [DllImport(Libraries.User32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(
        [In] nint hWnd,
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
