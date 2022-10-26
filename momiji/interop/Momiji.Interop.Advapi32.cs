using System.Runtime.InteropServices;

namespace Momiji.Interop.Advapi32;

internal static class Libraries
{
    public const string Advapi32 = "Advapi32.dll";
}

internal static class NativeMethods
{
    internal enum SE_OBJECT_TYPE : uint
    {
        SE_UNKNOWN_OBJECT_TYPE,
        SE_FILE_OBJECT,
        SE_SERVICE,
        SE_PRINTER,
        SE_REGISTRY_KEY,
        SE_LMSHARE,
        SE_KERNEL_OBJECT,
        SE_WINDOW_OBJECT,
        SE_DS_OBJECT,
        SE_DS_OBJECT_ALL,
        SE_PROVIDER_DEFINED_OBJECT,
        SE_WMIGUID_OBJECT,
        SE_REGISTRY_WOW64_32KEY,
        SE_REGISTRY_WOW64_64KEY
    };

    [Flags]
    internal enum SECURITY_INFORMATION : uint
    {
        OWNER_SECURITY_INFORMATION = 0x00000001,
        GROUP_SECURITY_INFORMATION = 0x00000002,
        DACL_SECURITY_INFORMATION = 0x00000004,
        SACL_SECURITY_INFORMATION = 0x00000008,
        UNPROTECTED_SACL_SECURITY_INFORMATION = 0x10000000,
        UNPROTECTED_DACL_SECURITY_INFORMATION = 0x20000000,
        PROTECTED_SACL_SECURITY_INFORMATION = 0x40000000,
        PROTECTED_DACL_SECURITY_INFORMATION = 0x80000000
    }

    [DllImport(Libraries.Advapi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetSecurityInfo(
      [In] IntPtr handle,
      [In] SE_OBJECT_TYPE ObjectType,
      [In] SECURITY_INFORMATION SecurityInfo,
      [Out] out IntPtr ppsidOwner,
      [Out] out IntPtr ppsidGroup,
      [Out] out IntPtr ppDacl,
      [Out] out IntPtr ppSacl,
      [Out] out IntPtr ppSecurityDescriptor
    );


    [DllImport(Libraries.Advapi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ConvertSidToStringSidW(
        [In] IntPtr sid,
        [Out] out IntPtr stringSid
    );

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 0)]
    internal struct Trustee
    {
        public IntPtr pMultipleTrustee;
        public int multipleTrusteeOperation;
        public int trusteeForm;
        public int trusteeType;
        public IntPtr pSid;
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 0)]
    internal struct SecurityAttributes
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    };


    [DllImport(Libraries.Advapi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetEffectiveRightsFromAclW(
        [In] IntPtr pacl,
        [In] ref Trustee pTrustee,
        [Out] out uint pAccessRights
    );


    internal enum SECURITY_DESCRIPTOR_CONST: int
    {
        REVISION = 1,
        MIN_LENGTH = 20
    }

    [DllImport(Libraries.Advapi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeSecurityDescriptor(
        [In] IntPtr pSecurityDescriptor,
        [In] SECURITY_DESCRIPTOR_CONST dwRevision
    );

    [DllImport(Libraries.Advapi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetSecurityDescriptorDacl(
        [In] IntPtr pSecurityDescriptor,
        [In] bool bDaclPresent,
        [In] IntPtr pDacl,
        [In] bool bDaclDefaulted
    );

    internal enum ACCESS_MODE: int
    {
        NOT_USED_ACCESS,
        GRANT_ACCESS,
        SET_ACCESS,
        DENY_ACCESS,
        REVOKE_ACCESS,
        SET_AUDIT_SUCCESS,
        SET_AUDIT_FAILURE
    }

    [Flags]
    internal enum ACE : int
    {
        NO_INHERITANCE = 0x0,
        OBJECT_INHERIT_ACE = 0x1,
        CONTAINER_INHERIT_ACE = 0x2,
        NO_PROPAGATE_INHERIT_ACE = 0x4,
        INHERIT_ONLY_ACE = 0x8
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 0)]
    internal struct ExplicitAccess
    {
        public int grfAccessPermissions;
        public ACCESS_MODE grfAccessMode;
        public ACE grfInheritance;
        public Trustee trustee;
    };

    [DllImport(Libraries.Advapi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int SetEntriesInAclW(
        [In] ulong cCountOfExplicitEntries,
        [In] IntPtr pListOfExplicitEntries,
        [In] IntPtr oldAcl,
        [Out] out IntPtr newAcl
    );

    

}
