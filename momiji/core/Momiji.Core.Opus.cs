using Microsoft.Extensions.Logging;
using Momiji.Core.Timer;
using Momiji.Core.Wave;
using Momiji.Interop.Buffer;
using Momiji.Interop.Opus;

namespace Momiji.Core.Opus;

public class OpusException : Exception
{
    public OpusException()
    {
    }

    public OpusException(string message) : base(message)
    {
    }

    public OpusException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class OpusOutputBuffer : IDisposable
{
    private bool _disposed;
    internal PinnedBuffer<byte[]>? Buffer { get; private set; }
    public BufferLog Log { get; }
    public int Wrote { get; internal set; }

    public OpusOutputBuffer(int size)
    {
        Buffer = new(new byte[size]);
        Log = new();
    }
    ~OpusOutputBuffer()
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

        Buffer?.Dispose();
        Buffer = null;

        _disposed = true;
    }
}

public class OpusEncoder : IDisposable
{
    private readonly ILogger _logger;
    private readonly ElapsedTimeCounter _counter;

    private bool _disposed;
    private Encoder? _encoder;

    public OpusEncoder(
        SamplingRate Fs,
        Channels channels,
        ILoggerFactory loggerFactory,
        ElapsedTimeCounter counter
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(counter);

        _logger = loggerFactory.CreateLogger<OpusEncoder>();
        _counter = counter;

        _logger.LogInformation($"opus version {NativeMethods.opus_get_version_string()}");

        _encoder =
            NativeMethods.opus_encoder_create(
                Fs, channels, OpusApplicationType.Audio, out var error
            );

        if (error != OpusStatusCode.OK)
        {
            throw new OpusException($"[opus] opus_encoder_create error:{NativeMethods.opus_strerror((int)error)}({error})");
        }
    }

    ~OpusEncoder()
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

        if (_encoder != null)
        {
            if (
                !_encoder.IsInvalid
                && !_encoder.IsClosed
            )
            {
                _encoder.Close();
            }
            _encoder = null;
        }

        _disposed = true;
    }

    public void Execute(
        PcmBuffer<float> source,
        OpusOutputBuffer dest
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dest);

        if (_encoder == default)
        {
            throw new InvalidOperationException("encoder is null.");
        }
        if (source.Buffer == default)
        {
            throw new InvalidOperationException("source.Buffer is null.");
        }
        if (dest.Buffer == default)
        {
            throw new InvalidOperationException("dest.Buffer is null.");
        }

        dest.Log.Marge(source.Log);

        dest.Log.Add("[opus] start opus_encode_float", _counter.NowTicks);
        dest.Wrote = _encoder.opus_encode_float(
            source.Buffer.AddrOfPinnedObject,
            source.Buffer.Target.Length / 2,
            dest.Buffer.AddrOfPinnedObject,
            dest.Buffer.Target.Length
            );
        /*
            この式を満たさないとダメ
            TODO 満たすように分結する仕組み要る？？？
            if (blockSize<samplingRate/400)
            return -1;
            if (400*blockSize!=samplingRate   && 200*blockSize!=samplingRate   && 100*blockSize!=samplingRate   &&
                50*blockSize!=samplingRate   &&  25*blockSize!=samplingRate   &&  50*blockSize!=3*samplingRate &&
                50*blockSize!=4*samplingRate &&  50*blockSize!=5*samplingRate &&  50*blockSize!=6*samplingRate)
            return -1;
        */
        dest.Log.Add($"[opus] end opus_encode_float {dest.Wrote}", _counter.NowTicks);
        if (dest.Wrote < 0)
        {
            throw new OpusException($"[opus] opus_encode_float error:{NativeMethods.opus_strerror(dest.Wrote)}({dest.Wrote})");
        }
    }
}
