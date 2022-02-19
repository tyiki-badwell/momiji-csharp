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
    private readonly IConfigurationSection _configurationSection;

    private readonly ILogger _logger;

    private bool _disposed;
    private readonly IDictionary<string, IntPtr> _dllPool = new ConcurrentDictionary<string, IntPtr>();

    public DllManager(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger<DllManager>();
        _configurationSection = configuration.GetSection($"{typeof(DllManager).FullName}:{(Environment.Is64BitProcess ? "x64" : "x86")}");

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
        _logger.LogInformation($"call SetDllDirectory({dllPathBase})");
        NativeMethods.SetDllDirectory(dllPathBase);

        try
        {
            NativeLibrary.SetDllImportResolver(assembly, ResolveDllImport);
        }
        catch (InvalidOperationException e)
        {
            _logger.LogInformation(e, "SetDllImportResolver failed.");
        }
    }
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _logger.LogInformation("[dll manager] dispose");
            foreach (var (libraryName, handle) in _dllPool)
            {
                NativeLibrary.Free(handle);
                _logger.LogInformation($"[dll manager] free {libraryName}");
            }
            _dllPool.Clear();
        }

        _disposed = true;
    }

    private IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        _logger.LogInformation($"call DllImportResolver({libraryName}, {assembly}, {searchPath})");
        var name = _configurationSection?[libraryName];
        if (name != default)
        {
            if (NativeLibrary.TryLoad(name, assembly, searchPath, out var handle))
            {
                _logger.LogInformation($"mapped {libraryName} -> {name}");
                return handle;
            }
        }
        return default;
    }

    private IntPtr TryLoad(string libraryName)
    {
        if (_dllPool.TryGetValue(libraryName, out var handle))
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

        if (!_dllPool.TryAdd(libraryName, handle))
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
