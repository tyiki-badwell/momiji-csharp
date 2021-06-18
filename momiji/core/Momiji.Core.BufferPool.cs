using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.Buffer
{
    public class BufferPool<T> : IDisposable, IReceivableSourceBlock<T>, ITargetBlock<T> where T : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        private bool disposed;
        private List<T> list = new();
        private readonly BufferBlock<T> bufferBlock = new();

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
                list.ForEach(item => item.Dispose());
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
            cancellationToken.ThrowIfCancellationRequested();
            return AddBuffer();
        }

        public bool TryReceive(Predicate<T> filter, out T item)
        {
            return bufferBlock.TryReceive(filter, out item);
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

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, T messageValue, ISourceBlock<T> source, bool consumeToAccept)
        {
            return ((ITargetBlock<T>)bufferBlock).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
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
            Logger.LogInformation($"AddBuffer[{GenericTypeName}] [{list.Count}]");
            return buffer;
        }
    }
}