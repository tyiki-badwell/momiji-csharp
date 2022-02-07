using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Interop.Kernel32;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Momiji.Core.Dll;

public interface IDllManager : IDisposable
{
    public T? GetExport<T>(string libraryName, string name) where T: Delegate;
}

public class DllManager : IDllManager
{
    private IConfigurationSection ConfigurationSection { get; }

    private ILogger Logger { get; }

    private bool disposed;
    private readonly IDictionary<string, IntPtr> dllPool = new ConcurrentDictionary<string, IntPtr>();

    public DllManager(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        Logger = loggerFactory.CreateLogger<DllManager>();

        {
            ArgumentNullException.ThrowIfNull(configuration);
            ConfigurationSection = configuration.GetSection($"{typeof(DllManager).FullName}:{(Environment.Is64BitProcess ? "x64" : "x86")}");
        }

        var assembly = Assembly.GetExecutingAssembly();

        var directoryName = Path.GetDirectoryName(assembly.Location);
        if (directoryName == default)
        {
            throw new InvalidOperationException($"GetDirectoryName({assembly.Location}) failed.");
        }

        var dllPathBase =
            Path.Combine(
                directoryName,
                "lib",
                Environment.Is64BitProcess ? "x64" : "x86"
            );
        Logger.LogInformation($"call SetDllDirectory({dllPathBase})");
        NativeMethods.SetDllDirectory(dllPathBase);

        try
        {
            NativeLibrary.SetDllImportResolver(assembly, ResolveDllImport);
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

    private IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        Logger.LogInformation($"call DllImportResolver({libraryName}, {assembly}, {searchPath})");
        var name = ConfigurationSection?[libraryName];
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

    public T? GetExport<T>(string libraryName, string name) where T: Delegate
    {
        ArgumentNullException.ThrowIfNull(libraryName);
        ArgumentNullException.ThrowIfNull(name);

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
