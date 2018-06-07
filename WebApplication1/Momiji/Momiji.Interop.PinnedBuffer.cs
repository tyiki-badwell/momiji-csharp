using System;
using System.Runtime.InteropServices;

namespace Momiji
{
    namespace Interop
    {
        public class PinnedBuffer<T> : IDisposable
        {
            private GCHandle handle;

            public PinnedBuffer(T buffer)
            {
                handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            }

            public void Dispose()
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }

            public IntPtr AddrOfPinnedObject()
            {
                return handle.AddrOfPinnedObject();
            }

            public T Target()
            {
                return (T)handle.Target;
            }
        }
    }
}
