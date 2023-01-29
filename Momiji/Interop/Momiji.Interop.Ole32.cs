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

    [DllImport(Libraries.Ole32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int CoGetApartmentType(
        [Out] out APTTYPE pAptType,
        [Out] out APTTYPEQUALIFIER pAptQualifier
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

    [DllImport(Libraries.Ole32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RoGetAgileReference(
        [In] AgileReferenceOptions options,
        [In] ref Guid riid,
        [In][MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        [Out] out IAgileReference ppAgileReference
    );

    [DllImport(Libraries.Ole32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int CoLockObjectExternal(
        [In][MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        [In][MarshalAs(UnmanagedType.Bool)] bool fLock,
        [In][MarshalAs(UnmanagedType.Bool)] bool fLastUnlockReleases
    );

}
