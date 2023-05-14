using System.Runtime.InteropServices;

namespace Momiji.Interop.Ole32;

internal static class Libraries
{
    public const string Ole32 = "Ole32.dll";
}

internal static class NativeMethods
{
    public enum APTTYPEQUALIFIER : int
    {
        NONE = 0,
        IMPLICIT_MTA = 1,
        NA_ON_MTA = 2,
        NA_ON_STA = 3,
        NA_ON_IMPLICIT_MTA = 4,
        NA_ON_MAINSTA = 5,
        APPLICATION_STA = 6,
        RESERVED_1 = 7
    }

    public enum APTTYPE : int
    {
        CURRENT = -1,
        STA = 0,
        MTA = 1,
        NA = 2,
        MAINSTA = 3
    }

    public enum THDTYPE : int
    {
        BLOCKMESSAGES = 0,
        PROCESSMESSAGES = 1
    }

    [DllImport(Libraries.Ole32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int CoGetApartmentType(
        [Out] out APTTYPE pAptType,
        [Out] out APTTYPEQUALIFIER pAptQualifier
    );

    [ComImport]
    [Guid("000001ce-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IComThreadingInfo
    {
        [PreserveSig]
        int GetCurrentApartmentType(
            [Out] out APTTYPE pAptType
        );

        [PreserveSig]
        int GetCurrentThreadType(
            [Out] out THDTYPE pThreadType
        );

        [PreserveSig]
        int GetCurrentLogicalThreadId(
            [Out] out Guid pguidLogicalThreadId
        );

        [PreserveSig]
        int SetCurrentLogicalThreadId(
            [In] ref Guid rguid
        );
    };

    [ComImport]
    [Guid("000001c0-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContext
    {
        [PreserveSig]
        int SetProperty(
            [In] ref Guid rpolicyId,
            [In] int flags, //need 0
            [In][MarshalAs(UnmanagedType.IUnknown)] object pUnk
        );
        
        [PreserveSig]
        int RemoveProperty(
            [In] ref Guid rPolicyId
        );
        
        [PreserveSig]
        int GetProperty(
            [In] ref Guid rGuid,
            [Out] out int pFlags,
            [Out][MarshalAs(UnmanagedType.IUnknown)] out object ppUnk
        );
        
        [PreserveSig]
        int EnumContextProps(
            [Out][MarshalAs(UnmanagedType.IUnknown)] out object /*IEnumContextProps*/ ppEnumContextProps
        );
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
    internal struct ContextProperty
    {
        public Guid policyId;
        public uint flags;
        public nint pUnk;
    }

    [ComImport]
    [Guid("000001c1-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumContextProps
    {
        [PreserveSig]
        int Next(
            [In] uint celt,
            [Out] out ContextProperty pContextProperties,
            [Out] out uint pceltFetched
        );

        [PreserveSig]
        int Skip(
            [In] uint celt
        );
        
        [PreserveSig]
        int Reset();
        
        [PreserveSig]
        int Clone(
            [Out][MarshalAs(UnmanagedType.IUnknown)] out object ppEnumContextProps
        );
        
        [PreserveSig]
        int Count(
            [Out] out uint pcelt
        );
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
    internal struct ComCallData
    {
        public uint dwDispid;
        public uint dwReserved;
        public nint pUserDefined;
    }

    [ComImport]
    [Guid("000001da-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextCallback
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ContextCall(
            [In] ref ComCallData pParam
        );
        
        [PreserveSig]
        int ContextCallback(
            [In] nint /*ContextCall*/ pfnCallback,
            [In] ref ComCallData pParam,
            [In] ref Guid riid,
            [In] int iMethod,
            [In][MarshalAs(UnmanagedType.IUnknown)] object pUnk
        );
    };

    [DllImport(Libraries.Ole32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int CoGetObjectContext(
        [In] ref Guid riid,
        [Out][MarshalAs(UnmanagedType.IUnknown)] out object ppv
    );

    [ComImport]
    [Guid("C03F6A43-65A4-9818-987E-E0B810D2A6F2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAgileReference
    {
        [PreserveSig]
        int Resolve(
            [In] ref Guid riid,
            [Out][MarshalAs(UnmanagedType.IUnknown)] out object ppvObjectReference
        );
    }

    internal enum AgileReferenceOptions
    {
        DEFAULT = 0,
        DELAYEDMARSHAL = 1,
    };

    [DllImport(Libraries.Ole32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RoGetAgileReference(
        [In] AgileReferenceOptions options,
        [In] ref Guid riid,
        [In][MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        [Out] out IAgileReference ppAgileReference
    );

    [DllImport(Libraries.Ole32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int CoLockObjectExternal(
        [In][MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        [In][MarshalAs(UnmanagedType.Bool)] bool fLock,
        [In][MarshalAs(UnmanagedType.Bool)] bool fLastUnlockReleases
    );

}
