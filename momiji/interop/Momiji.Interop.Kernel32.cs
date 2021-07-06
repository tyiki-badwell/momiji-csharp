using System;
using System.Runtime.InteropServices;

namespace Momiji.Interop.Kernel32
{
    public static class SafeNativeMethods
    {
        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern bool SetDllDirectory(
            [In]   string lpPathName
        );


        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public int message;
            public IntPtr wParam;
            public IntPtr lParam;
            public int time;
            public int pt_x;
            public int pt_y;
        }

        [DllImport("user32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int GetMessage(
            ref MSG msg, IntPtr hwnd, int nMsgFilterMin, int nMsgFilterMax);

        [DllImport("user32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern bool PeekMessage(
            ref MSG msg, IntPtr hwnd, int nMsgFilterMin, int nMsgFilterMax, int wRemoveMsg);

        [DllImport("user32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern bool TranslateMessage(
            ref MSG msg);

        [DllImport("user32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr DispatchMessage(
            ref MSG msg);

        [DllImport("user32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern bool PostMessage(
            IntPtr hWnd, int nMsg, IntPtr wParam, IntPtr lParam);

    }
}