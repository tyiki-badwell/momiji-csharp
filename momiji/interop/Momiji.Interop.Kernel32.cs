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
    }
}