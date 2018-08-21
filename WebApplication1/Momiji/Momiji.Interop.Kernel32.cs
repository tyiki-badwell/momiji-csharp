using System;
using System.Diagnostics;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Permissions;

namespace Momiji.Interop
{
    public class Kernel32
    {
        public enum ACCESS_TYPES : UInt32
        {
            //  The following are masks for the predefined standard access types
            DELETE = 0x00010000,
            READ_CONTROL = 0x00020000,
            WRITE_DAC = 0x00040000,
            WRITE_OWNER = 0x00080000,
            SYNCHRONIZE = 0x00100000,
            STANDARD_RIGHTS_REQUIRED = 0x000F0000,
            STANDARD_RIGHTS_READ = READ_CONTROL,
            STANDARD_RIGHTS_WRITE = READ_CONTROL,
            STANDARD_RIGHTS_EXECUTE = READ_CONTROL,
            STANDARD_RIGHTS_ALL = 0x001F0000,
            SPECIFIC_RIGHTS_ALL = 0x0000FFFF,
            // AccessSystemAcl access type
            ACCESS_SYSTEM_SECURITY = 0x01000000,
            // MaximumAllowed access type
            MAXIMUM_ALLOWED = 0x02000000,
            //  These are the generic rights.
            GENERIC_READ = 0x80000000,
            GENERIC_WRITE = 0x40000000,
            GENERIC_EXECUTE = 0x20000000,
            GENERIC_ALL = 0x10000000,


            KEY_QUERY_VALUE = 0x0001,
            KEY_SET_VALUE = 0x0002,
            KEY_CREATE_SUB_KEY = 0x0004,
            KEY_ENUMERATE_SUB_KEYS = 0x0008,
            KEY_NOTIFY = 0x0010,
            KEY_CREATE_LINK = 0x0020,
            KEY_WOW64_32KEY = 0x0200,
            KEY_WOW64_64KEY = 0x0100,
            KEY_WOW64_RES = 0x0300,

            KEY_READ = ((STANDARD_RIGHTS_READ
                                            | KEY_QUERY_VALUE
                                            | KEY_ENUMERATE_SUB_KEYS
                                            | KEY_NOTIFY)
                                            & ~SYNCHRONIZE),


            KEY_WRITE = ((STANDARD_RIGHTS_WRITE
                                            | KEY_SET_VALUE
                                            | KEY_CREATE_SUB_KEY)
                                            & ~SYNCHRONIZE),

            KEY_EXECUTE = (KEY_READ & ~SYNCHRONIZE),

            KEY_ALL_ACCESS = ((STANDARD_RIGHTS_ALL
                                            | KEY_QUERY_VALUE
                                            | KEY_SET_VALUE
                                            | KEY_CREATE_SUB_KEY
                                            | KEY_ENUMERATE_SUB_KEYS
                                            | KEY_NOTIFY
                                            | KEY_CREATE_LINK)
                                            & ~SYNCHRONIZE),

        };

        public enum SHARE_MODE : UInt32
        {
            FILE_SHARE_NONE = 0x00000000,
            FILE_SHARE_READ = 0x00000001,
            FILE_SHARE_WRITE = 0x00000002,
            FILE_SHARE_DELETE = 0x00000004,
        };

        public enum CREATION_DISPOSITION : UInt32
        {
            CREATE_NEW = 1,
            CREATE_ALWAYS = 2,
            OPEN_EXISTING = 3,
            OPEN_ALWAYS = 4,
            TRUNCATE_EXISTING = 5,
        };

        public enum FLAG_AND_ATTRIBUTE : UInt32
        {
            FILE_ATTRIBUTE_READONLY = 0x00000001,
            FILE_ATTRIBUTE_HIDDEN = 0x00000002,
            FILE_ATTRIBUTE_SYSTEM = 0x00000004,
            FILE_ATTRIBUTE_DIRECTORY = 0x00000010,
            FILE_ATTRIBUTE_ARCHIVE = 0x00000020,
            FILE_ATTRIBUTE_DEVICE = 0x00000040,
            FILE_ATTRIBUTE_NORMAL = 0x00000080,
            FILE_ATTRIBUTE_TEMPORARY = 0x00000100,
            FILE_ATTRIBUTE_SPARSE_FILE = 0x00000200,
            FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400,
            FILE_ATTRIBUTE_COMPRESSED = 0x00000800,
            FILE_ATTRIBUTE_OFFLINE = 0x00001000,
            FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 0x00002000,
            FILE_ATTRIBUTE_ENCRYPTED = 0x00004000,
            FILE_ATTRIBUTE_VIRTUAL = 0x00010000,

            FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000,
            FILE_FLAG_OPEN_NO_RECALL = 0x00100000,
            FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000,
            FILE_FLAG_POSIX_SEMANTICS = 0x01000000,
            FILE_FLAG_BACKUP_SEMANTICS = 0x02000000,
            FILE_FLAG_DELETE_ON_CLOSE = 0x04000000,
            FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000,
            FILE_FLAG_RANDOM_ACCESS = 0x10000000,
            FILE_FLAG_NO_BUFFERING = 0x20000000,
            FILE_FLAG_OVERLAPPED = 0x40000000,
            FILE_FLAG_WRITE_THROUGH = 0x80000000,
        };

        [SecurityPermission(SecurityAction.InheritanceDemand, UnmanagedCode = true)]
        [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
        internal class DynamicLinkLibrary : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
        {
            public DynamicLinkLibrary() : base(true)
            {
                Trace.WriteLine("DynamicLinkLibrary");
            }

            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
            protected override Boolean ReleaseHandle()
            {
                Trace.WriteLine("FreeLibrary");
                return FreeLibrary(handle);
            }
        };

        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern DynamicLinkLibrary LoadLibrary(
            [In]   string lpFileName
        );

        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        internal static extern Boolean FreeLibrary(
            [In]   IntPtr hModule
        );

        [DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, SetLastError = true)]
        internal static extern IntPtr GetProcAddress(
                [In]   DynamicLinkLibrary hModule,
                [In]   string lpProcName
        );

        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern Boolean SetDllDirectory(
            [In]   string lpPathName
        );

        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern Boolean CopyMemory(
            [In]   IntPtr Destination,
            [In]   IntPtr Source,
            [In]   long Length
        );

        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr FillMemory(
            [In]   IntPtr Destination,
            [In]   long Length,
            [In]   byte Fill
        );
    }
}