using System;
using System.Runtime.InteropServices;

namespace Momiji.Interop
{
    public class PinnedBuffer<T> : IDisposable where T : class
    {
        private bool disposed = false;
        private GCHandle handle;

        public PinnedBuffer(T buffer)
        {
            handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
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
                if (handle != null && handle.IsAllocated)
                {
                    handle.Free();
                }
            }

            disposed = true;
        }

        public IntPtr AddrOfPinnedObject()
        {
            return handle.AddrOfPinnedObject();
        }

        public T Target
        {
            get
            {
                return (T)handle.Target;
            }
        }
    }
}