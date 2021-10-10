using System;
using System.Runtime.InteropServices;

namespace Momiji.Interop.Kernel32
{
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
            [In] string lpModuleName
        );
    }
}