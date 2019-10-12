using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Momiji.Interop
{
    internal class BufferLog
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
            Marge(source.Log);
        }

        public void Marge(List<(string, double)> source)
        {
            Clear();
            Log.InsertRange(0, source);
        }

        public List<(string label, double time)> Copy()
        {
            return new List<(string, double)>(Log);
        }

        public double GetSpentTime()
        {
            return Log[Log.Count - 1].time - Log[0].time;
        }

        public double GetFirstTime()
        {
            return Log[0].time;
        }
    }

    public class PinnedBuffer<T> : IDisposable where T : class
    {
        private bool disposed = false;
        private GCHandle handle;
        internal BufferLog Log { get; private set; }

        public PinnedBuffer(T buffer)
        {
            handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            Log = new BufferLog();
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

            if (disposing)
            {
                Log.Clear();
            }

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


    public class PinnedDelegate<T> : IDisposable where T : class
    {
        private bool disposed = false;
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