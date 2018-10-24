using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core
{
    public class BufferPool<T> : IDisposable, IReceivableSourceBlock<T>, ITargetBlock<T> where T : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        private bool disposed = false;
        private List<T> list = new List<T>();
        private BufferBlock<T> bufferBlock = new BufferBlock<T>();
        
        public Task Completion => bufferBlock.Completion;

        ~BufferPool()
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
                Logger.LogInformation($"Dispose buffer size[{list.Count}][{GenericTypeName}]");
                foreach (var item in list)
                {
                    item.Dispose();
                }
                list.Clear();
                list = null;
            }

            disposed = true;
        }

        public T Receive(CancellationToken cancellationToken)
        {
            var result = bufferBlock.TryReceive(out T item);
            if (result)
            {
                return item;
            }
            Logger.LogInformation($"Receive AddBuffer[{GenericTypeName}]");
            return AddBuffer();
        }

        public bool TryReceive(Predicate<T> filter, out T item)
        {
            var result = bufferBlock.TryReceive(filter, out item);
            if (result)
            {
                return true;
            }
            Logger.LogInformation($"TryReceive AddBuffer[{GenericTypeName}]");
            item = AddBuffer();
            return true;
        }

        public bool TryReceiveAll(out IList<T> items)
        {
            return bufferBlock.TryReceiveAll(out items);
        }

        public T ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
        {
            return ((IReceivableSourceBlock<T>)bufferBlock).ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        public IDisposable LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions)
        {
            return bufferBlock.LinkTo(target, linkOptions);
        }

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            ((IReceivableSourceBlock<T>)bufferBlock).ReleaseReservation(messageHeader, target);
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            return ((IReceivableSourceBlock<T>)bufferBlock).ReserveMessage(messageHeader, target);
        }

        public void Complete()
        {
            bufferBlock.Complete();
        }

        public void Fault(Exception exception)
        {
            ((IReceivableSourceBlock<T>)bufferBlock).Fault(exception);
        }

        public delegate T Allocator();
        private Allocator A { get; }
        private string GenericTypeName { get; }

        public BufferPool(int size, Allocator a, ILoggerFactory loggerFactory)
        {
            GenericTypeName = string.Join(",", GetType().GetGenericArguments().Select(type => type.Name));

            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<BufferPool<T>>();

            A = a;
            for (var i = 0; i < size; i++)
            {
                bufferBlock.Post(AddBuffer());
            }
        }

        private T AddBuffer()
        {
            var buffer = A();
            list.Add(buffer);
            return buffer;
        }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, T messageValue, ISourceBlock<T> source, bool consumeToAccept)
        {
            return ((ITargetBlock<T>)bufferBlock).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }

        /*
        public BufferBlock<T> MakeBufferBlock()
        {
            var result = MakeEmptyBufferBlock();
            foreach (var item in list)
            {
                result.Post(item);
            }
            return result;
        }

        /*
        public BufferBlock<T> MakeEmptyBufferBlock()
        {
            return new BufferBlock<T>();
        }
        */
    }
}