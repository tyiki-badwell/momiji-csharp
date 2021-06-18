using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Momiji.Interop.Buffer
{
    public class BufferLog
    {
        private List<(string label, double time)> Log { get; }

        public BufferLog()
        {
            Log = new List<(string, double)>();
        }

        public void Clear()
        {
            Log.Clear();
        }

        public void Add(string label, double time)
        {
            Log.Add((label, time));
        }

        public void Marge(BufferLog source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(paramName: nameof(source));
            }
            Marge(source.Log);
        }

        public void Marge(IEnumerable<(string, double)> source)
        {
            Clear();
            Log.InsertRange(0, source);
        }

        public void ForEach(Action<(string label, double time)> action)
        {
            Log.ForEach(action);
        }

        public double SpentTime()
        {
            return Log[^1].time - Log[0].time;
        }

        public double FirstTime()
        {
            return Log[0].time;
        }
    }

    public class PinnedBuffer<T> : IDisposable
    {
        private bool disposed;
        private GCHandle handle;

        public PinnedBuffer(T buffer)
        {
            handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        }

        ~PinnedBuffer()
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

            if (handle.IsAllocated)
            {
                handle.Free();
            }

            disposed = true;
        }

        public T Target
        {
            get
            {
                return (T)handle.Target;
            }
        }

        public IntPtr AddrOfPinnedObject
        {
            get
            {
                return handle.AddrOfPinnedObject();
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:識別子は、不適切なサフィックスを含むことはできません", Justification = "<保留中>")]
    public class PinnedDelegate<T> : IDisposable where T : class
    {
        private bool disposed;
        private GCHandle handle;

        public PinnedDelegate(T buffer)
        {
            handle = GCHandle.Alloc(buffer, GCHandleType.Normal);
        }

        ~PinnedDelegate()
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
            }

            if (handle.IsAllocated)
            {
                handle.Free();
            }

            disposed = true;
        }

        public IntPtr FunctionPointer
        {
            get
            {
                return Marshal.GetFunctionPointerForDelegate(handle.Target);
            }
        }
    }


}