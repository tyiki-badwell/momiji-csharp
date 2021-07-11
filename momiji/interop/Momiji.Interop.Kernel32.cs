using System;
using System.Runtime.InteropServices;

namespace Momiji.Interop.Kernel32
{
    internal static class Libraries
    {
        public const string Kernel32 = "kernel32.dll";
        public const string User32 = "user32.dll";
    }

    internal static class SafeNativeMethods
    {
        [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern bool SetDllDirectory(
            [In] string lpPathName
        );

        [DllImport(Libraries.Kernel32, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
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

        public delegate IntPtr WNDPROC(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport(Libraries.User32, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern ushort RegisterClass(ref WNDCLASS lpWndClass);


        [DllImport(Libraries.User32, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr CreateWindowEx(
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

        [DllImport(Libraries.User32, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern bool DestroyWindow(IntPtr hwnd);

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

        [DllImport(Libraries.User32, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int GetMessage(
            IntPtr/*ref MSG*/ msg, IntPtr hwnd, int nMsgFilterMin, int nMsgFilterMax);

        [DllImport(Libraries.User32, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern bool PeekMessage(
            IntPtr/*ref MSG*/ msg, IntPtr hwnd, int nMsgFilterMin, int nMsgFilterMax, int wRemoveMsg);

        [DllImport(Libraries.User32, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern bool TranslateMessage(
            IntPtr/*ref MSG*/ msg);

        [DllImport(Libraries.User32, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr DispatchMessage(
            IntPtr/*ref MSG*/ msg);

        [DllImport(Libraries.User32, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern bool PostMessage(
            IntPtr hWnd, int nMsg, IntPtr wParam, IntPtr lParam);

    }
}