using System;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Permissions;

namespace Momiji.Core
{
    [SecurityPermission(SecurityAction.InheritanceDemand, UnmanagedCode = true)]
    [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
    internal class Dll : SafeHandle
    {
        public override bool IsInvalid => handle == IntPtr.Zero;

        public Dll(string libraryPath) : base(IntPtr.Zero, true)
        {
            NativeLibrary.TryLoad(libraryPath, Assembly.GetExecutingAssembly(), DllImportSearchPath.UserDirectories, out handle);
        }

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        protected override bool ReleaseHandle()
        {
            NativeLibrary.Free(handle);
            return true;
        }


        public T GetExport<T>(string name)
        {
            if (NativeLibrary.TryGetExport(handle, name, out var address))
            {
                return Marshal.GetDelegateForFunctionPointer<T>(address);
            }
            else
            {
                return default;
            }
        }
    };

}
