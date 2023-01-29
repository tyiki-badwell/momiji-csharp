using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Momiji.Core.Cache;

public abstract class PoolValue : IDisposable
{
    public enum PoolValueStatus : int
    {
        Free,
        Rent,
        WaitingToRun,
        Running,
        Canceling,
        RanToCompletion,
        Canceled,
        Faulted,
        Created
    }

    private int _status = (int)PoolValueStatus.Created;

    public PoolValueStatus Status
    {
        get => (PoolValueStatus)_status;
        private set => _status = (int)value;
    }

    internal PoolValue()
    {
    }

    ~PoolValue()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected abstract void Dispose(bool disposing);

    internal void Free()
    {
        Status = PoolValueStatus.Free;
    }

    internal void Rent()
    {
        Status = PoolValueStatus.Rent;
    }

    internal void WaitingToRun()
    {
        Status = PoolValueStatus.WaitingToRun;
    }

    internal void RanToCompletion()
    {
        Status = PoolValueStatus.RanToCompletion;
    }

    internal void Canceled()
    {
        Status = PoolValueStatus.Canceled;
    }

    internal void Faulted()
    {
        Status = PoolValueStatus.Faulted;
    }

    public void Invoke()
    {
        if ((int)PoolValueStatus.WaitingToRun == Interlocked.CompareExchange(ref _status, (int)PoolValueStatus.Running, (int)PoolValueStatus.WaitingToRun))
        {
            InvokeCore(false);
        }
        else
        {
            InvokeCore(true);
        }
    }

    protected abstract void InvokeCore(bool ignore);

    public void Cancel()
    {
        if ((int)PoolValueStatus.WaitingToRun == Interlocked.CompareExchange(ref _status, (int)PoolValueStatus.Canceling, (int)PoolValueStatus.WaitingToRun))
        {
            CancelCore(false);
        }
        else
        {
            CancelCore(true);
        }
    }

    protected abstract void CancelCore(bool ignore);
}

public class Pool<TKey, TValue> : IDisposable
    where TKey : notnull
    where TValue : notnull, PoolValue
{
    private readonly ILogger _logger;

    private bool _disposed;

    private readonly ConcurrentStack<(TKey, TValue)> _cache = new();
    private readonly ConcurrentStack<(TKey, TValue)> _avail = new();
    private readonly ConcurrentDictionary<TKey, TValue> _busy = new();

    private readonly Func<(TKey, TValue)> _allocator;

    public Pool(
        Func<(TKey, TValue)> allocator,
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger<Pool<TKey, TValue>>();
        _allocator = allocator;
    }

    public TValue Get()
    {
        if (!_avail.TryPop(out var item))
        {
            item = Add();
        }

        _logger.LogTrace($"busy {item.Item1}");
        _busy.TryAdd(item.Item1, item.Item2);
        item.Item2.Rent();

        return item.Item2;
    }

    private (TKey, TValue) Add()
    {
        var (key, value) = _allocator();
        _logger.LogTrace($"create {key}");
        _cache.Push((key, value));

        return (key, value);
    }

    public void Release(TKey key)
    {
        if (_busy.TryRemove(key, out var value))
        {
            _logger.LogTrace($"release {key}");
            _avail.Push((key, value));
            value.Free();
        }
        else
        {
            _logger.LogWarning($"not busy {key}");
        }
    }

    ~Pool()
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
            _logger.LogDebug($"busy items {_busy.Count}");

            while (!_busy.IsEmpty)
            {
                foreach (var key in _busy.Keys)
                {
                    _logger.LogDebug($"try cancel busy key {key}");
                    if (_busy.TryGetValue(key, out var result))
                    {
                        _logger.LogDebug($"{key} {result.Status}");
                        result.Cancel();
                    }
                }

                _logger.LogDebug($"wait ...");
                Task.Delay(10).Wait();
            }

            _logger.LogDebug($"avail items {_avail.Count}");
            _logger.LogDebug($"cache items {_cache.Count}");

            _avail.Clear();

            while (_cache.TryPop(out var result))
            {
                result.Item2.Dispose();
            }
            _cache.Clear();
        }

        _disposed = true;
    }

}
