using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Interop.Kernel32;

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
    private readonly IDictionary<string, nint> _dllPool = new ConcurrentDictionary<string, nint>();

    public DllManager(
        IConfiguration configuration, 
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger<DllManager>();

        Setup();

        _configurationSection = configuration.GetSection($"{typeof(DllManager).FullName}:{(Environment.Is64BitProcess ? "x64" : "x86")}");

        //TODO DLLリダイレクトにしたい

        var assembly = Assembly.GetExecutingAssembly();

        var directoryName = Path.GetDirectoryName(assembly.Location);
        if (directoryName == default)
        {
            throw new InvalidOperationException($"Path.GetDirectoryName({assembly.Location}) failed.");
        }

        var dllPathBase =
            Path.Combine(
                directoryName,
                "lib",
                Environment.Is64BitProcess ? "x64" : "x86"
            );

        if (!Path.Exists(dllPathBase))
        {
            throw new InvalidOperationException($"Path.Exists({dllPathBase}) failed.");
        }

        _logger.LogInformation($"call SetDllDirectory({dllPathBase})");
        if (!NativeMethods.SetDllDirectoryW(dllPathBase))
        {
            var error = Marshal.GetLastPInvokeError();
            _logger.LogError($"SetDllDirectory({dllPathBase}) failed. {error} {Marshal.GetPInvokeErrorMessage(error)}");
        }

        try
        {
            NativeLibrary.SetDllImportResolver(assembly, ResolveDllImport);
        }
        catch (InvalidOperationException e)
        {
            _logger.LogInformation(e, "SetDllImportResolver failed.");
        }
    }

    private void Setup()
    {
        {
            var info = new NativeMethods.SYSTEM_INFO();
            NativeMethods.GetNativeSystemInfo(ref info);

            _logger.LogInformation($"GetNativeSystemInfo wProcessorArchitecture {info.wProcessorArchitecture}");
            _logger.LogInformation($"GetNativeSystemInfo dwPageSize {info.dwPageSize}");
            _logger.LogInformation($"GetNativeSystemInfo lpMinimumApplicationAddress {info.lpMinimumApplicationAddress}");
            _logger.LogInformation($"GetNativeSystemInfo lpMaximumApplicationAddress {info.lpMaximumApplicationAddress}");
            _logger.LogInformation($"GetNativeSystemInfo dwActiveProcessorMask {info.dwActiveProcessorMask}");
            _logger.LogInformation($"GetNativeSystemInfo dwNumberOfProcessors {info.dwNumberOfProcessors}");
            _logger.LogInformation($"GetNativeSystemInfo dwProcessorType {info.dwProcessorType}");
            _logger.LogInformation($"GetNativeSystemInfo dwAllocationGranularity {info.dwAllocationGranularity}");
            _logger.LogInformation($"GetNativeSystemInfo wProcessorLevel {info.wProcessorLevel}");
            _logger.LogInformation($"GetNativeSystemInfo wProcessorRevision {info.wProcessorRevision}");
        }

        try
        {
            if (!NativeMethods.IsWow64Process2(Process.GetCurrentProcess().Handle, out var processMachine, out var nativeMachine))
            {
                var error = Marshal.GetLastPInvokeError();
                _logger.LogError($"IsWow64Process2 failed. {error} {Marshal.GetPInvokeErrorMessage(error)}");
            }
            else
            {
                _logger.LogInformation($"IsWow64Process2 {processMachine:X} {nativeMachine:X}");
            }
        }
        catch (EntryPointNotFoundException e)
        {
            _logger.LogError(e, "IsWow64Process2 failed.");
        }

        try
        {
            var policy = NativeMethods.GetSystemDEPPolicy();
            _logger.LogInformation($"GetSystemDEPPolicy {policy}");

            if (!NativeMethods.GetProcessDEPPolicy(Process.GetCurrentProcess().Handle, out var flags, out var permanent))
            {
                var error = Marshal.GetLastPInvokeError();
                _logger.LogError($"GetProcessDEPPolicy failed. {error} {Marshal.GetPInvokeErrorMessage(error)}");
            }
            else
            {
                _logger.LogInformation($"GetProcessDEPPolicy {flags} {permanent}");

                if (!permanent)
                {
                    //変更可能ならDEPをON
                    if (!NativeMethods.SetProcessDEPPolicy(NativeMethods.PROCESS_DEP.ENABLE | NativeMethods.PROCESS_DEP.DISABLE_ATL_THUNK_EMULATION))
                    {
                        var error = Marshal.GetLastPInvokeError();
                        _logger.LogError($"SetProcessDEPPolicy failed. {error} {Marshal.GetPInvokeErrorMessage(error)}");
                    }
                }
            }
        }
        catch(EntryPointNotFoundException e)
        {
            _logger.LogError(e, "DEP check failed.");
        }

        try
        {
            //セーフ検索モードをON　＋　解除不可
            if (!NativeMethods.SetSearchPathMode(NativeMethods.BASE_SEARCH_PATH.ENABLE_SAFE_SEARCHMODE | NativeMethods.BASE_SEARCH_PATH.PERMANENT))
            {
                var error = Marshal.GetLastPInvokeError();
                _logger.LogError($"SetSearchPathMode(BASE_SEARCH_PATH.ENABLE_SAFE_SEARCHMODE+PERMANENT) failed. {error} {Marshal.GetPInvokeErrorMessage(error)}");
            }
        }
        catch (EntryPointNotFoundException e)
        {
            _logger.LogError(e, "SetSearchPathMode failed.");
        }

        //DLL検索順からカレントディレクトリを削除
        if (!NativeMethods.SetDllDirectoryW(""))
        {
            var error = Marshal.GetLastPInvokeError();
            _logger.LogError($"SetDllDirectory('') failed. {error} {Marshal.GetPInvokeErrorMessage(error)}");
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

    private nint ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
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

    private nint TryLoad(string libraryName)
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
