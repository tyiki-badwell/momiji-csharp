using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Momiji.Core.SharedMemory;

public class SharedMemoryException : Exception
{
    public SharedMemoryException()
    {
    }

    public SharedMemoryException(string message) : base(message)
    {
    }

    public SharedMemoryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class IPCBuffer<T> : IDisposable where T : notnull
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly string _mapName;

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _va;

    private int _allocatedOffset;

    private static readonly int SIZE_OF_T = Marshal.SizeOf<T>();

    public IPCBuffer(
        string? mapName, 
        int size,
        ILoggerFactory loggerFactory
    )
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = _loggerFactory.CreateLogger<IPCBuffer<T>>();

        ArgumentNullException.ThrowIfNull(mapName);
        if (mapName.Length == 0)
        {
            throw new ArgumentNullException(nameof(mapName));
        }

        _mapName = mapName + ":" + Guid.NewGuid().ToString();

        _mmf = MemoryMappedFile.CreateOrOpen(_mapName, SIZE_OF_T * size);
        _va = _mmf.CreateViewAccessor();

        _logger.LogInformation($"[memory] create mapName:{_mapName} capacity:{_va.Capacity}");
    }

    ~IPCBuffer()
    {
        Dispose(false);
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
            _va?.Dispose();
            _mmf?.Dispose();
        }

        _logger.LogInformation($"[memory] dispose mapName:{_mapName}");
        _disposed = true;
    }

    public Span<T> AsSpan(int offset, int length)
    {
        unsafe
        {
            var ptr = GetPtr();
            return new Span<T>((ptr + (SIZE_OF_T * offset)), length);
        }
    }

    public nint Allocate(int count)
    {
        if (_va == null)
        {
            throw new InvalidOperationException("va is null.");
        }

        var size = SIZE_OF_T * count;
        if (_va.Capacity < _allocatedOffset + size)
        {
            throw new SharedMemoryException($"over capacity[{_va.Capacity}] allocatedOffset[{_allocatedOffset}] size[{size}]");
        }

        var result = GetIntPtr(_allocatedOffset);

        _allocatedOffset += size;
        return result;
    }

    public nint GetIntPtr(int offset)
    {
        unsafe
        {
            var ptr = GetPtr();
            return new nint(ptr) + offset;
        }
    }

    internal unsafe byte* GetPtr()
    {
        if (_va == null)
        {
            throw new InvalidOperationException("va is null.");
        }

        unsafe
        {
            byte* ptr = default;
            _va.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            return ptr;
        }
    }
}
