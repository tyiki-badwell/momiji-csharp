using Microsoft.Extensions.Logging;
using Momiji.Core.Timer;
using Momiji.Core.Vst;
using Momiji.Core.Wave;

namespace Momiji.Core.Trans;

public class ToPcm<T> : IDisposable where T : struct
{
    private readonly ILogger _logger;
    private readonly ElapsedTimeCounter _counter;

    private bool _disposed;

    public ToPcm(
        ILoggerFactory loggerFactory,
        ElapsedTimeCounter counter
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(counter);

        _logger = loggerFactory.CreateLogger<ToPcm<T>>();
        _counter = counter;
    }

    ~ToPcm()
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

        if (disposing)
        {

        }

        _disposed = true;
    }

    public void Execute(
        VstBuffer<T> source,
        PcmBuffer<T> dest
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dest);

        dest.Log.Marge(source.Log);

        var targetIdx = 0;
        var target = new Span<T>(dest.Buffer.Target);
        var left = new ReadOnlySpan<T>(source.GetChannelBuffer(0));
        var right = new ReadOnlySpan<T>(source.GetChannelBuffer(1));

        dest.Log.Add("[to pcm] start", _counter.NowTicks);
        for (var idx = 0; idx < left.Length; idx++)
        {
            target[targetIdx++] = left[idx];
            target[targetIdx++] = right[idx];
        }
        dest.Log.Add("[to pcm] end", _counter.NowTicks);
    }
    public void Execute(
        VstBuffer2<T> source,
        PcmBuffer<T> dest
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dest);

        dest.Log.Marge(source.Log);

        var targetIdx = 0;
        var target = new Span<T>(dest.Buffer.Target);
        unsafe
        {
            var left = new ReadOnlySpan<T>(source.GetChannelBuffer(0).ToPointer(), source.BlockSize);
            var right = new ReadOnlySpan<T>(source.GetChannelBuffer(1).ToPointer(), source.BlockSize);

            dest.Log.Add("[to pcm] start", _counter.NowTicks);
            for (var idx = 0; idx < left.Length; idx++)
            {
                target[targetIdx++] = left[idx];
                target[targetIdx++] = right[idx];
            }
        }
        dest.Log.Add("[to pcm] end", _counter.NowTicks);
    }
}
