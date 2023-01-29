using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.Buffer;
public class BufferPool<T> : IDisposable, IReceivableSourceBlock<T>, ITargetBlock<T> where T : notnull, IDisposable
{
    private readonly ILogger _logger;

    private bool _disposed;
    private readonly List<T> _list = new();
    private readonly BufferBlock<T> _bufferBlock = new();

    public Task Completion => _bufferBlock.Completion;

    public delegate T Allocator();
    private readonly Allocator _allocator;
    private readonly string _genericTypeName;

    public BufferPool(int size, Allocator allocator, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger<BufferPool<T>>();

        _genericTypeName = string.Join(",", GetType().GetGenericArguments().Select(type => type.Name));

        _allocator = allocator;
        for (var i = 0; i < size; i++)
        {
            _bufferBlock.Post(AddBuffer());
        }
    }

    private T AddBuffer()
    {
        var buffer = _allocator();
        _list.Add(buffer);
        _logger.LogInformation("AddBuffer[{_genericTypeName}] [{Count}]", _genericTypeName, _list.Count);
        return buffer;
    }
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
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger.LogInformation("Dispose [{_genericTypeName}] buffer size[{_list.Count}]", _genericTypeName, _list.Count);
            _list.ForEach(item => item.Dispose());
            _list.Clear();
        }

        _disposed = true;
    }

    public T Receive(CancellationToken cancellationToken)
    {
        if (_bufferBlock.TryReceive(out var item))
        {
            return item;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return AddBuffer();
    }

    public bool TryReceive(Predicate<T>? filter, [MaybeNullWhen(false)] out T item)
    {
        return _bufferBlock.TryReceive(filter, out item);
    }

    public bool TryReceiveAll([MaybeNullWhen(false)] out IList<T> items)
    {
        return _bufferBlock.TryReceiveAll(out items);
    }

    [return: MaybeNull]
    public T ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
    {
        return ((IReceivableSourceBlock<T>)_bufferBlock).ConsumeMessage(messageHeader, target, out messageConsumed);
    }

    public IDisposable LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions)
    {
        return _bufferBlock.LinkTo(target, linkOptions);
    }

    public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
    {
        ((IReceivableSourceBlock<T>)_bufferBlock).ReleaseReservation(messageHeader, target);
    }

    public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
    {
        return ((IReceivableSourceBlock<T>)_bufferBlock).ReserveMessage(messageHeader, target);
    }

    public void Complete()
    {
        _bufferBlock.Complete();
    }

    public void Fault(Exception exception)
    {
        ((IReceivableSourceBlock<T>)_bufferBlock).Fault(exception);
    }

    public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, T messageValue, ISourceBlock<T>? source, bool consumeToAccept)
    {
        return ((ITargetBlock<T>)_bufferBlock).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
    }

}
