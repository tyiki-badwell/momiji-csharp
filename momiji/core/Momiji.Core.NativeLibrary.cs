using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Interop.Kernel32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Momiji.Core
{
    public interface IDllManager : IDisposable
    {
        public T GetExport<T>(string libraryName, string name);
    }

    public class DllManager : IDllManager
    {
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        private bool disposed = false;
        private readonly IDictionary<string, IntPtr> dllPool = new ConcurrentDictionary<string, IntPtr>();

        public DllManager(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<DllManager>();

            // TODO ディレクトリ構成のハードコードを止める
            var dllPathBase =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "lib",
                    Environment.Is64BitProcess ? "64" : "32"
                );
            Logger.LogInformation($"call SetDllDirectory({dllPathBase})");
            SafeNativeMethods.SetDllDirectory(dllPathBase);

            try
            {
                NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), ResolveDllImport);
            }
            catch (InvalidOperationException e)
            {
                Logger.LogInformation(e, "SetDllImportResolver failed.");
            }
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;

            if (disposing)
            {
                Logger.LogInformation("[dll manager] dispose");
                foreach (var (libraryName, handle) in dllPool)
                {
                    NativeLibrary.Free(handle);
                    Logger.LogInformation($"[dll manager] free {libraryName}");
                }
                dllPool.Clear();
            }

            disposed = true;
        }

        internal IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            //TODO データ構造の定義がプログラムになっているのでよくない
            Logger.LogInformation($"call DllImportResolver({libraryName}, {assembly}, {searchPath})");
            var name = Configuration.GetSection("LibraryNameMapping:" + (Environment.Is64BitProcess ? "64" : "32"))?[libraryName];
            if (name != default)
            {
                if (NativeLibrary.TryLoad(name, assembly, searchPath, out var handle))
                {
                    Logger.LogInformation($"mapped {libraryName} -> {name}");
                    return handle;
                }
            }
            return default;
        }

        private IntPtr TryLoad(string libraryName)
        {
            if (dllPool.TryGetValue(libraryName, out var handle))
            {
                return handle;
            }

            var assembly = Assembly.GetExecutingAssembly();
            var searchPath = DllImportSearchPath.UserDirectories | DllImportSearchPath.UseDllDirectoryForDependencies;

            handle = ResolveDllImport(libraryName, assembly, searchPath);
            if (handle == default)
            {
                handle = NativeLibrary.Load(libraryName, assembly, searchPath);
            }

            if (!dllPool.TryAdd(libraryName, handle))
            {
                NativeLibrary.Free(handle);
            }
            return handle;
        }

        public T GetExport<T>(string libraryName, string name)
        {
            var handle = TryLoad(libraryName);
            if (NativeLibrary.TryGetExport(handle, name, out var address))
            {
                return Marshal.GetDelegateForFunctionPointer<T>(address);
            }
            else
            {
                return default;
            }
        }
    }
}
