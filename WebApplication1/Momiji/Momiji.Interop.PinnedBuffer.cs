﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Momiji.Interop
{
    public class BufferLog
    {
        private List<Tuple<string, double>> Log { get; }

        public BufferLog()
        {
            Log = new List<Tuple<string, double>>();
        }

        public void Clear()
        {
            Log.Clear();
        }

        public void Add(string label, double time)
        {
            Log.Add(new Tuple<string, double>(label, time));
        }

        public void Marge(BufferLog source)
        {
            Marge(source.Log);
        }

        public void Marge(List<Tuple<string, double>> source)
        {
            Clear();
            Log.InsertRange(0, source);
        }

        public List<Tuple<string, double>> Copy()
        {
            return new List<Tuple<string, double>>(Log);
        }

        public double GetSpentTime()
        {
            return Log[Log.Count - 1].Item2 - Log[0].Item2;
        }
    }

    public class PinnedBuffer<T> : IDisposable where T : class
    {
        private bool disposed = false;
        private GCHandle handle;
        public BufferLog Log { get; }

        public PinnedBuffer(T buffer)
        {
            handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            Log = new BufferLog();
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

        public IntPtr AddrOfPinnedObject {
            get
            {
                return handle.AddrOfPinnedObject();
            }
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