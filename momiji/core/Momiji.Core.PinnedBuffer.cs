using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Momiji.Core.Buffer;

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
        ArgumentNullException.ThrowIfNull(source);
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

public abstract class InternalGCHandleBuffer<T> : IDisposable where T : notnull
{
    private bool _disposed;
    protected GCHandle Handle { get; }

    protected InternalGCHandleBuffer([DisallowNull]T buffer, GCHandleType handleType)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Handle = GCHandle.Alloc(buffer, handleType);
    }

    ~InternalGCHandleBuffer()
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
        if (_disposed) return;

        if (Handle.IsAllocated)
        {
            Handle.Free();
        }

        _disposed = true;
    }
}

public class PinnedBuffer<T> : InternalGCHandleBuffer<T> where T : notnull
{
    public PinnedBuffer(T buffer): base(buffer, GCHandleType.Pinned)
    {
    }

    [NotNull]
    public T Target
    {
        get
        {
            var result = Handle.Target;
            if (result == default)
            {
                throw new InvalidOperationException("Target is null.");
            }
            return (T)result;
        }
    }
    public IntPtr AddrOfPinnedObject
    {
        get
        {
            return Handle.AddrOfPinnedObject();
        }
    }
}

public class PinnedDelegate<T> : InternalGCHandleBuffer<T> where T : notnull, Delegate
{
    public PinnedDelegate(T buffer) : base(buffer, GCHandleType.Normal)
    {
    }

    public IntPtr FunctionPointer
    {
        get
        {
            if (Handle.Target == default)
            {
                return default;
            }
            else
            {
                return Marshal.GetFunctionPointerForDelegate(Handle.Target);
            }
        }
    }
}
