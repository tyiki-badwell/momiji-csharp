using System.Runtime.InteropServices;

namespace Momiji.Interop.User32;

internal static class Libraries
{
    public const string User32 = "user32.dll";
}

internal static class NativeMethods
{
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
