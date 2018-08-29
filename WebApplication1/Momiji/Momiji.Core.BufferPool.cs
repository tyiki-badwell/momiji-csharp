using System;
using System.Collections.Generic;
using System.Threading.Tasks.Dataflow;

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
                list.Clear();
                list = null;
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
            var result = makeEmptyBufferBlock();
            foreach (var item in list)
            {
                result.Post(item);
            }
            return result;
        }

        public BufferBlock<T> makeEmptyBufferBlock()
        {
            return new BufferBlock<T>();
        }

    }
}