using System;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Permissions;

namespace Momiji.Interop.Kernel32
{
    public static class DLLMethod
    {
        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetDllDirectory(
            [In]   string lpPathName
        );
    }
}