using System;
using System.Runtime.InteropServices;

namespace Momiji.Interop.Kernel32
{
    internal static class Libraries
    {
        public const string Kernel32 = "kernel32.dll";
        public const string User32 = "user32.dll";
        public const string Gdi32 = "gdi32.dll";
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
            [In] string lpModuleName
        );


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASS
        {
            [Flags]
            public enum CS : uint
            {
                VREDRAW = 0x0001,
                HREDRAW = 0x0002,
                DBLCLKS = 0x0008,
                DROPSHADOW = 0x00020000,
                SAVEBITS = 0x0800
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
        internal static extern uint MsgWaitForMultipleObjects(
            [In] uint nCount,
            [In] IntPtr pHandles,
            [In][MarshalAs(UnmanagedType.Bool)] bool fWaitAll,
            [In] uint dwMilliseconds,
            [In] uint dwWakeMask
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

        [DllImport(Libraries.Gdi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr CreateCompatibleDC(
            [In] HandleRef hdc
        );

        [DllImport(Libraries.Gdi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteDC(
            [In] HandleRef hdc
        );

        [DllImport(Libraries.Gdi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr CreateCompatibleBitmap(
            [In] HandleRef hdc,
            [In] int cx,
            [In] int cy
        );

        [DllImport(Libraries.Gdi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(
            [In] HandleRef ho
        );

        [DllImport(Libraries.Gdi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr SelectObject(
            [In] HandleRef hdc,
            [In] HandleRef h
        );


        [DllImport(Libraries.Gdi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BitBlt(
            [In] HandleRef hdc,
            [In] int x,
            [In] int y,
            [In] int cx,
            [In] int cy,
            [In] HandleRef hdcSrc,
            [In] int x1,
            [In] int y1,
            [In] uint rop
        );
    }
}