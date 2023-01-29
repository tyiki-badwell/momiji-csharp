using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Core.Window;
using Momiji.Interop.User32;
using Advapi32 = Momiji.Interop.Advapi32.NativeMethods;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using Ole32 = Momiji.Interop.Ole32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Internal.Debug;

public class WindowDebug
{
    public static void CheckIntegrityLevel(
        ILoggerFactory loggerFactory
    )
    {
        var logger = loggerFactory.CreateLogger<WindowDebug>();

        if (!Advapi32.OpenProcessToken(
                Process.GetCurrentProcess().Handle,
                Advapi32.DesiredAccess.TOKEN_QUERY,
                out var token
        ))
        {
            var error = Marshal.GetLastPInvokeError();
            logger.LogError($"[window debug] OpenProcessToken failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            return;
        }

        try
        {
            if (!Advapi32.GetTokenInformation(
                    token,
                    Advapi32.TOKEN_INFORMATION_CLASS.TokenIntegrityLevel,
                    nint.Zero,
                    0,
                    out var dwLengthNeeded
            ))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error != 122) //ERROR_INSUFFICIENT_BUFFER ではない
                {
                    logger.LogError($"[window debug] GetTokenInformation failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
                    return;
                }
            }
            else
            { //ここでは必ずエラーになるハズなので、正常系がエラー扱い
                return;
            }

            using var tiBuf = new PinnedBuffer<byte[]>(new byte[dwLengthNeeded]);
            var ti = tiBuf.AddrOfPinnedObject;

            if (!Advapi32.GetTokenInformation(
                    token,
                    Advapi32.TOKEN_INFORMATION_CLASS.TokenIntegrityLevel,
                    ti,
                    dwLengthNeeded,
                    out var _
            ))
            {
                var error = Marshal.GetLastPInvokeError();
                logger.LogError($"[window debug] GetTokenInformation failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
                return;
            }

            var p = Marshal.ReadIntPtr(ti);
            var sid = new SecurityIdentifier(p);
            logger.LogInformation($"[window debug] SecurityInfo token:({sid})");
            PrintAccountFromSid(logger, p);
        }
        finally
        {
            token.Dispose();
        }
    }

    private static void PrintDesktopACL(
        ILogger logger,
        HDesktop desktop
    )
    {
        try
        {
            var windowSecurity = new WindowSecurity(desktop);
            logger.LogInformation($"[window debug] SecurityInfo owner:{windowSecurity.GetOwner(typeof(NTAccount))}({windowSecurity.GetOwner(typeof(SecurityIdentifier))})");
            logger.LogInformation($"[window debug] SecurityInfo group:{windowSecurity.GetGroup(typeof(NTAccount))}({windowSecurity.GetGroup(typeof(SecurityIdentifier))})");

            logger.LogInformation("---------------------------------");
            foreach (AccessRule<User32.DESKTOP_ACCESS_MASK> rule in windowSecurity.GetAccessRules(true, true, typeof(NTAccount)))
            {
                logger.LogInformation($"[window debug] AccessRule:{rule.IdentityReference} {rule.Rights}");
            }
            logger.LogInformation("---------------------------------");
            foreach (AccessRule<User32.DESKTOP_ACCESS_MASK> rule in windowSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                logger.LogInformation($"[window debug] AccessRule:{rule.IdentityReference} {rule.Rights}");
            }
            logger.LogInformation("---------------------------------");
        }
        catch (Exception e)
        {
            logger.LogError(e, "[window debug] WindowSecurity error");
        }
    }
    private static void PrintAccountFromSid(
    ILogger logger,
        nint sid
    )
    {
        using var szNameBuf = new PinnedBuffer<int[]>(new int[1]);
        var szName = szNameBuf.AddrOfPinnedObject;

        using var szDomainNameBuf = new PinnedBuffer<int[]>(new int[1]);
        var szDomainName = szDomainNameBuf.AddrOfPinnedObject;

        if (!Advapi32.LookupAccountSidW(nint.Zero, sid, nint.Zero, szName, nint.Zero, szDomainName, out var use))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != 122) //ERROR_INSUFFICIENT_BUFFER ではない
            {
                logger.LogError($"LookupAccountSidW failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
                return;
            }
        }
        else
        { //ここでは必ずエラーになるハズなので、正常系がエラー扱い
            return;
        }

        using var nameBuf = new PinnedBuffer<char[]>(new char[szNameBuf.Target[0]]);
        var name = nameBuf.AddrOfPinnedObject;

        using var domainNameBuf = new PinnedBuffer<char[]>(new char[szDomainNameBuf.Target[0]]);
        var domainName = domainNameBuf.AddrOfPinnedObject;
        if (!Advapi32.LookupAccountSidW(nint.Zero, sid, name, szName, domainName, szDomainName, out var _))
        {
            var error = Marshal.GetLastPInvokeError();
            logger.LogError($"GetTokenInformation failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            return;
        }
        logger.LogInformation($"account [{Marshal.PtrToStringUni(name)}] [{Marshal.PtrToStringUni(domainName)}] {use}");
    }

    public static void CheckDesktop(
        ILoggerFactory loggerFactory
    )
    {
        var logger = loggerFactory.CreateLogger<WindowDebug>();

        {
            using var desktop = User32.GetThreadDesktop(Kernel32.GetCurrentThreadId());
            logger.LogInformation($"[window debug] GetThreadDesktop now:{desktop.DangerousGetHandle():X}");

            if (desktop.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                logger.LogError($"[window debug] GetThreadDesktop failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }
            else
            {
                PrintDesktopACL(logger, desktop);
            }
        }

        {
            var sd =
                new CommonSecurityDescriptor(
                    false,
                    false,
                    ControlFlags.None,
                    new SecurityIdentifier(WellKnownSidType.AnonymousSid, null),
                    null,
                    null,
                    null
                );

            var length = sd.BinaryLength;
            var buffer = new byte[length];

            sd.GetBinaryForm(buffer, 0);

            using var aa = new PinnedBuffer<byte[]>(buffer);

            var sa = new Advapi32.SecurityAttributes()
            {
                nLength = Marshal.SizeOf<Advapi32.SecurityAttributes>(),
                lpSecurityDescriptor = aa.AddrOfPinnedObject,
                bInheritHandle = false
            };

            //TODO error 1307
            using var desktop =
                User32.CreateDesktopW(
                    "test",
                    nint.Zero,
                    nint.Zero,
                    User32.DF.NONE,
                    User32.DESKTOP_ACCESS_MASK.GENERIC_ALL,
                    ref sa
                );
            logger.LogInformation($"[window debug] CreateDesktopW new:{desktop.DangerousGetHandle():X}");

            if (desktop.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                logger.LogError($"[window debug] CreateDesktopW failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }
            else
            {
                PrintDesktopACL(logger, desktop);
            }
        }

        /*
        {
            using var sdBuf = new PinnedBuffer<byte[]>(new byte[(int)Advapi32.SECURITY_DESCRIPTOR_CONST.MIN_LENGTH * 10]);
            var sd = sdBuf.AddrOfPinnedObject;

            {
                var result =
                    Advapi32.InitializeSecurityDescriptor(
                        sd,
                        Advapi32.SECURITY_DESCRIPTOR_CONST.REVISION
                    );
                if (!result)
                {
                    var error = Marshal.GetLastPInvokeError();
                    logger.LogError($"[window debug] InitializeSecurityDescriptor failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
                }
            }

            {
                using var eaBuf = new PinnedBuffer<Advapi32.ExplicitAccess>(new()
                {
                    grfAccessPermissions = 0,
                    grfAccessMode = Advapi32.ACCESS_MODE.GRANT_ACCESS,
                    grfInheritance = Advapi32.ACE.NO_INHERITANCE,
                    trustee = new Advapi32.Trustee()
                    {
                        trusteeForm = 0,
                        trusteeType = 0,
                        pSid = nint.Zero
                    }
                });

                //TODO error 87 
                var error =
                    Advapi32.SetEntriesInAclW(
                        1,
                        eaBuf.AddrOfPinnedObject,
                        nint.Zero,
                        out var newAcl
                    );
                if (error != 0)
                {
                    logger.LogError($"[window debug] SetEntriesInAclW failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
                }
                else
                {
                    var result =
                        Advapi32.SetSecurityDescriptorDacl(
                            sd,
                            true,
                            newAcl,
                            false
                        );
                    if (!result)
                    {
                        var error2 = Marshal.GetLastPInvokeError();
                        logger.LogError($"[window debug] SetSecurityDescriptorDacl failed [{error2} {Marshal.GetPInvokeErrorMessage(error2)}]");
                    }
                }
                Marshal.FreeHGlobal(newAcl);
            }

            var sa = new Advapi32.SecurityAttributes()
            {
                nLength = Marshal.SizeOf<Advapi32.SecurityAttributes>(),
                lpSecurityDescriptor = sd,
                bInheritHandle = false
            };

            using var desktop =
                User32.CreateDesktopW(
                    "test",
                    nint.Zero,
                    nint.Zero,
                    User32.DF.NONE,
                    User32.DESKTOP_ACCESS_MASK.GENERIC_ALL,
                    ref sa
                );
            logger.LogInformation($"[window debug] CreateDesktopW new:{desktop.DangerousGetHandle():X}");

            if (desktop.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                logger.LogError($"[window debug] CreateDesktopW failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }
            else
            {
                PrintDesktopACL(logger, desktop);
            }
        }
            */
    }

    public static void CheckGetProcessInformation(
        ILoggerFactory loggerFactory
    )
    {
        var logger = loggerFactory.CreateLogger<WindowDebug>();

        {
            using var buf = new PinnedBuffer<Kernel32.MEMORY_PRIORITY_INFORMATION>(new());
            var result =
                Kernel32.GetProcessInformation(
                    Process.GetCurrentProcess().Handle,
                    Kernel32.PROCESS_INFORMATION_CLASS.ProcessMemoryPriority,
                    buf.AddrOfPinnedObject,
                    buf.SizeOf
                );
            var error = Marshal.GetLastPInvokeError();
            logger.LogInformation($"[window debug] GetProcessInformation ProcessMemoryPriority:{result} {buf.SizeOf} [{error} {Marshal.GetPInvokeErrorMessage(error)}] {buf.Target.MemoryPriority:F}");
        }

        {
            using var buf = new PinnedBuffer<Kernel32.PROCESS_POWER_THROTTLING_STATE>(new()
            {
                Version = 1
            });

            //TODO error 87 
            var result =
                Kernel32.GetProcessInformation(
                    Process.GetCurrentProcess().Handle,
                    Kernel32.PROCESS_INFORMATION_CLASS.ProcessPowerThrottling,
                    buf.AddrOfPinnedObject,
                    buf.SizeOf
                );
            var error = Marshal.GetLastPInvokeError();
            logger.LogInformation($"[window debug] GetProcessInformation ProcessPowerThrottling:{result} {buf.SizeOf} [{error} {Marshal.GetPInvokeErrorMessage(error)}] {buf.Target.Version} {buf.Target.ControlMask} {buf.Target.StateMask}");
        }
    }
}

public class ThreadDebug
{
    public readonly struct ApartmentType
    {
        internal readonly Ole32.APTTYPE AptType;
        internal readonly Ole32.APTTYPEQUALIFIER AptQualifier;
        internal readonly int ManagedThreadId;

        internal ApartmentType(Ole32.APTTYPE aptType, Ole32.APTTYPEQUALIFIER aptQualifier) => (AptType, AptQualifier, ManagedThreadId) = (aptType, aptQualifier, Environment.CurrentManagedThreadId);

        public override string ToString() => $"AptType:{AptType} AptQualifier:{AptQualifier} ManagedThreadId:{ManagedThreadId:X}";
    }


    public static ApartmentType GetApartmentType()
    {
        var _ = Ole32.CoGetApartmentType(out var pAptType, out var pAptQualifier);
        return new ApartmentType(pAptType, pAptQualifier);
    }
}