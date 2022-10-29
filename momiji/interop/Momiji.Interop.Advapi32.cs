using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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


    internal enum DesiredAccess : uint
    {
        STANDARD_RIGHTS_REQUIRED = 0x000F0000,
        STANDARD_RIGHTS_READ = 0x00020000,
        TOKEN_ASSIGN_PRIMARY = 0x0001,
        TOKEN_DUPLICATE = 0x0002,
        TOKEN_IMPERSONATE = 0x0004,
        TOKEN_QUERY = 0x0008,
        TOKEN_QUERY_SOURCE = 0x0010,
        TOKEN_ADJUST_PRIVILEGES = 0x0020,
        TOKEN_ADJUST_GROUPS = 0x0040,
        TOKEN_ADJUST_DEFAULT = 0x0080,
        TOKEN_ADJUST_SESSIONID = 0x0100,
        TOKEN_READ = 
            STANDARD_RIGHTS_READ
            | TOKEN_QUERY,
        TOKEN_ALL_ACCESS = 
            STANDARD_RIGHTS_REQUIRED
            | TOKEN_ASSIGN_PRIMARY 
            | TOKEN_DUPLICATE 
            | TOKEN_IMPERSONATE 
            | TOKEN_QUERY 
            | TOKEN_QUERY_SOURCE 
            | TOKEN_ADJUST_PRIVILEGES 
            | TOKEN_ADJUST_GROUPS 
            | TOKEN_ADJUST_DEFAULT 
            | TOKEN_ADJUST_SESSIONID
    }

    [DllImport(Libraries.Advapi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(
        [In] IntPtr ProcessHandle,
        [In] DesiredAccess DesiredAccess,
        [Out] out HToken TokenHandle
    );

    internal enum TOKEN_INFORMATION_CLASS : int
    {
        TokenUser = 1,
        TokenGroups,
        TokenPrivileges,
        TokenOwner,
        TokenPrimaryGroup,
        TokenDefaultDacl,
        TokenSource,
        TokenType,
        TokenImpersonationLevel,
        TokenStatistics,
        TokenRestrictedSids,
        TokenSessionId,
        TokenGroupsAndPrivileges,
        TokenSessionReference,
        TokenSandBoxInert,
        TokenAuditPolicy,
        TokenOrigin,
        TokenElevationType,
        TokenLinkedToken,
        TokenElevation,
        TokenHasRestrictions,
        TokenAccessInformation,
        TokenVirtualizationAllowed,
        TokenVirtualizationEnabled,
        TokenIntegrityLevel,
        TokenUIAccess,
        TokenMandatoryPolicy,
        TokenLogonSid,
        TokenIsAppContainer,
        TokenCapabilities,
        TokenAppContainerSid,
        TokenAppContainerNumber,
        TokenUserClaimAttributes,
        TokenDeviceClaimAttributes,
        TokenRestrictedUserClaimAttributes,
        TokenRestrictedDeviceClaimAttributes,
        TokenDeviceGroups,
        TokenRestrictedDeviceGroups,
        TokenSecurityAttributes,
        TokenIsRestricted,
        TokenProcessTrustLevel,
        TokenPrivateNameSpace,
        TokenSingletonAttributes,
        TokenBnoIsolation,
        TokenChildProcessFlags,
        TokenIsLessPrivilegedAppContainer,
        TokenIsSandboxed,
        TokenIsAppSilo,
        MaxTokenInfoClass
    }

    [DllImport(Libraries.Advapi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(
        [In] this HToken TokenHandle,
        [In] TOKEN_INFORMATION_CLASS TokenInformationClass,
        [In] IntPtr TokenInformation,
        [In] int TokenInformationLength,
        [Out] out int ReturnLength
    );

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 0)]
    internal struct SidAndAttributes
    {
        public int Sid;
        public int Attributes;
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 0)]
    internal struct TokenMandatoryLabel
    {
        public SidAndAttributes Label;
    };

    [DllImport(Libraries.Advapi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupAccountSidW(
        [In] IntPtr lpSystemName,
        [In] IntPtr Sid,
        [In] IntPtr Name,
        [In] IntPtr cchName,
        [In] IntPtr ReferencedDomainName,
        [In] IntPtr cchReferencedDomainName,
        [Out] out int peUse
    );

}

internal sealed class HToken : SafeHandleZeroOrMinusOneIsInvalid
{
    public HToken() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return Kernel32.NativeMethods.CloseHandle(handle);
    }
}
