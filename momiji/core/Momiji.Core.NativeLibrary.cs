using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Interop.Kernel32;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Permissions;

namespace Momiji.Core
{
    [SecurityPermission(SecurityAction.InheritanceDemand, UnmanagedCode = true)]
    [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
    public sealed class Dll : SafeHandle
    {
        public static void Setup(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger<Dll>();
            var dllPathBase =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "lib",
                    Environment.Is64BitProcess ? "64" : "32"
                );
            logger.LogInformation($"call SetDllDirectory({dllPathBase})");
            SafeNativeMethods.SetDllDirectory(dllPathBase);

            if (configuration != null)
            {
                //TODO データ構造の定義がプログラムになっているのでよくない
                try
                {
                    NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), (libraryName, assembly, searchPath) =>
                    {
                        logger.LogInformation($"call DllImportResolver({libraryName}, {assembly}, {searchPath})");
                        var name = configuration.GetSection("LibraryNameMapping:" + (Environment.Is64BitProcess ? "64" : "32"))?[libraryName];
                        if (name != default)
                        {
                            if (NativeLibrary.TryLoad(name, assembly, searchPath, out var handle))
                            {
                                logger.LogInformation($"mapped {libraryName} -> {name}");
                                return handle;
                            }
                        }
                        return default;
                    });
                }
                catch(InvalidOperationException e)
                {
                    logger.LogInformation(e, "SetDllImportResolver failed.");
                }
            }
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        public Dll(string libraryPath) : base(IntPtr.Zero, true)
        {
            handle = NativeLibrary.Load(libraryPath, Assembly.GetExecutingAssembly(), DllImportSearchPath.UserDirectories);
        }

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        protected override bool ReleaseHandle()
        {
            NativeLibrary.Free(handle);
            return true;
        }


        public T GetExport<T>(string name)
        {
            if (!IsInvalid && NativeLibrary.TryGetExport(handle, name, out var address))
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
