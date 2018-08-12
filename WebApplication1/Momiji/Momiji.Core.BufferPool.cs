﻿using Momiji.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Dataflow;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;

namespace Momiji.Core
{
    public class BufferPool<T> : IDisposable where T : IDisposable
    {
        private bool disposed = false;
        private List<T> list = new List<T>();

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
                foreach(var item in list)
                {
                    item.Dispose();
                }
            }

            disposed = true;
        }

        public delegate T Allocator();

        public BufferPool(int size, Allocator a)
        {
            for (var i = 0; i < size; i++)
            {
                list.Add(a());
            }
        }

        public BufferBlock<T> makeBufferBlock()
        {
            var result = new BufferBlock<T>();
            foreach (var item in list)
            {
                result.Post(item);
            }
            return result;
        }
    }
}