using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Interop.Advapi32;
using Momiji.Interop.User32;
using Advapi32 = Momiji.Interop.Advapi32.NativeMethods;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Internal.Debug;

internal class WindowDebug
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
            logger.LogError($"[window debug] OpenProcessToken failed {Marshal.GetLastWin32Error()}");
            return;
        }

        try
        {
            if (!Advapi32.GetTokenInformation(
                    token,
                    Advapi32.TOKEN_INFORMATION_CLASS.TokenIntegrityLevel,
                    IntPtr.Zero,
                    0,
                    out var dwLengthNeeded
            ))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 122) //ERROR_INSUFFICIENT_BUFFER ではない
                {
                    logger.LogError($"[window debug] GetTokenInformation failed {error}");
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
                logger.LogError($"[window debug] GetTokenInformation failed {Marshal.GetLastWin32Error()}");
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

            var rules = windowSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier));
            for (var idx = 0; idx < rules.Count; idx++)
            {
                var rule = (AccessRule<int>?)rules[idx];
                if (rule == null)
                {
                    continue;
                }
                logger.LogInformation($"[window debug] AccessRule:{rule.IdentityReference} {Enum.ToObject(typeof(User32.DESKTOP_ACCESS_MASK), rule.Rights)}");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "[window debug] WindowSecurity error");
        }
    }
    private static void PrintAccountFromSid(
    ILogger logger, 
        IntPtr sid
    )
    {
        using var szNameBuf = new PinnedBuffer<int[]>(new int[1]);
        var szName = szNameBuf.AddrOfPinnedObject;

        using var szDomainNameBuf = new PinnedBuffer<int[]>(new int[1]);
        var szDomainName = szDomainNameBuf.AddrOfPinnedObject;

        if (!Advapi32.LookupAccountSidW(IntPtr.Zero, sid, IntPtr.Zero, szName, IntPtr.Zero, szDomainName, out var use))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 122) //ERROR_INSUFFICIENT_BUFFER ではない
            {
                logger.LogError($"LookupAccountSidW failed {error}");
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
        if (!Advapi32.LookupAccountSidW(IntPtr.Zero, sid, name, szName, domainName, szDomainName, out var _))
        {
            logger.LogError($"GetTokenInformation failed {Marshal.GetLastWin32Error()}");
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
                logger.LogError($"[window debug] GetThreadDesktop failed {Marshal.GetLastWin32Error()}");
            }
            else
            {
                PrintDesktopACL(logger, desktop);
            }
        }

        /*
        {
            new RawSecurityDescriptor(
                ControlFlags.None
                );


        }
        */

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
                    logger.LogError($"[window debug] InitializeSecurityDescriptor failed {Marshal.GetLastWin32Error()}");
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
                        pSid = IntPtr.Zero
                    }
                });

                var error =
                    Advapi32.SetEntriesInAclW(
                        1,
                        eaBuf.AddrOfPinnedObject,
                        IntPtr.Zero,
                        out var newAcl
                    );
                if (error != 0)
                {
                    logger.LogError($"[window debug] SetEntriesInAclW failed {error}");
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
                        logger.LogError($"[window debug] SetSecurityDescriptorDacl failed {Marshal.GetLastWin32Error()}");
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
                    IntPtr.Zero,
                    IntPtr.Zero,
                    User32.DF.NONE,
                    User32.DESKTOP_ACCESS_MASK.GENERIC_ALL,
                    ref sa
                );
            logger.LogInformation($"[window debug] CreateDesktopW new:{desktop.DangerousGetHandle():X}");

            if (desktop.IsInvalid)
            {
                logger.LogError($"[window debug] CreateDesktopW failed {Marshal.GetLastWin32Error()}");
            }
            else
            {
                PrintDesktopACL(logger, desktop);
            }
        }
    }

}
