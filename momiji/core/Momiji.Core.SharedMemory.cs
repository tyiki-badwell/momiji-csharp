using Microsoft.Extensions.Logging;
using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Momiji.Core.SharedMemory
{
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

    public class IPCBuffer<T> : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private bool disposed;

        private string MapName { get; }

        private MemoryMappedFile mmf;
        private MemoryMappedViewAccessor va;

        private int allocatedOffset;

        private static readonly int SIZE_OF_T = Marshal.SizeOf<T>();

        public IPCBuffer(
            string mapName, 
            int size,
            ILoggerFactory loggerFactory
        )
        {
            LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            Logger = LoggerFactory.CreateLogger<IPCBuffer<T>>();

            if (mapName == default || mapName.Length == 0)
            {
                throw new ArgumentNullException(nameof(mapName));
            }

            MapName = mapName + ":" + Guid.NewGuid().ToString();

            mmf = MemoryMappedFile.CreateOrOpen(MapName, SIZE_OF_T * size);
            va = mmf.CreateViewAccessor();

            Logger.LogInformation($"[memory] create mapName:{MapName} capacity:{va.Capacity}");
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
            if (disposed) return;

            if (disposing)
            {
                va?.Dispose();
                va = default;

                mmf?.Dispose();
                mmf = default;
            }

            Logger.LogInformation($"[memory] dispose mapName:{MapName}");
            disposed = true;
        }

        public Span<T> AsSpan(int offset, int length)
        {
            unsafe
            {
                byte* ptr = GetPtr();
                return new Span<T>((ptr + (SIZE_OF_T * offset)), length);
            }
        }

        public IntPtr Allocate(int count)
        {
            var size = Marshal.SizeOf<T>() * count;
            if (va.Capacity < allocatedOffset + size)
            {
                throw new SharedMemoryException($"over capacity[{va.Capacity}] allocatedOffset[{allocatedOffset}] size[{size}]");
            }

            var result = GetIntPtr(allocatedOffset);

            allocatedOffset += size;
            return result;
        }

        public IntPtr GetIntPtr(int offset)
        {
            unsafe
            {
                byte* ptr = GetPtr();
                return new IntPtr(ptr) + offset;
            }
        }

        internal unsafe byte* GetPtr()
        {
            unsafe
            {
                byte* ptr = default;
                va.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                return ptr;
            }
        }
    }
}