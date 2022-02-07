using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Core.Timer;
using Momiji.Interop.Buffer;
using Momiji.Interop.Wave;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.Wave;

public class WaveException : Exception
{

    public WaveException()
    {
    }

    public WaveException(string message) : base(message)
    {
    }

    public WaveException(string message, Exception innerException) : base(message, innerException)
    {
    }

    internal WaveException(MMRESULT mmResult) : base(MakeMessage(mmResult))
    {
    }

    static private string MakeMessage(MMRESULT mmResult)
    {
        var text = new System.Text.StringBuilder(256);
        NativeMethods.waveOutGetErrorText(mmResult, text, (uint)text.Capacity);
        return $"{text}({mmResult})";
    }
}

public class PcmBuffer<T> : IDisposable where T : struct
{
    private bool _disposed;
    internal PinnedBuffer<T[]> Buffer { get; }
    public BufferLog Log { get; }
    public PcmBuffer(int blockSize, int channels)
    {
        Buffer = new(new T[blockSize * channels]);
        Log = new();
    }
    ~PcmBuffer()
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
        _disposed = true;
    }
}

public sealed class WaveOutShort : WaveOut<short>
{
    public WaveOutShort(
        int deviceID,
        short channels,
        int samplesPerSecond,
        SPEAKER channelMask,
        ILoggerFactory loggerFactory,
        ElapsedTimeCounter counter,
        ITargetBlock<PcmBuffer<short>> sourceReleaseQueue
    ) : base(
        deviceID,
        channels,
        samplesPerSecond,
        channelMask,
        new Guid("00000001-0000-0010-8000-00aa00389b71"),
        loggerFactory,
        counter,
        sourceReleaseQueue
        )
    { }
}

public sealed class WaveOutFloat : WaveOut<float>
{
    public WaveOutFloat(
        int deviceID,
        short channels,
        int samplesPerSecond,
        SPEAKER channelMask,
        ILoggerFactory loggerFactory,
        ElapsedTimeCounter counter,
        ITargetBlock<PcmBuffer<float>> sourceReleaseQueue
    ) : base(
        deviceID,
        channels,
        samplesPerSecond,
        channelMask,
        new Guid("00000003-0000-0010-8000-00aa00389b71"),
        loggerFactory,
        counter,
        sourceReleaseQueue
        )
    { }
}

internal class WaveHeaderBuffer : PinnedBuffer<WaveHeader>
{
    internal WaveHeaderBuffer() : base(new WaveHeader())
    {
    }
}

[Flags]
public enum SPEAKER
{
    None,
    FrontLeft = 0x00000001,
    FrontRight = 0x00000002,
    FrontCenter = 0x00000004,
    LowFrequency = 0x00000008,
    BackLeft = 0x00000010,
    BackRight = 0x00000020,
    FrontLeftOfCenter = 0x00000040,
    FrontRightOfCenter = 0x00000080,
    BackCenter = 0x00000100,
    SideLeft = 0x00000200,
    SideRight = 0x00000400,
    TopCenter = 0x00000800,
    TopFrontLeft = 0x00001000,
    TopFrontCenter = 0x00002000,
    TopFrontRight = 0x00004000,
    TopBackLeft = 0x00008000,
    TopBackCenter = 0x00010000,
    TopBackRight = 0x00020000,
}

public class WaveOut<T> : IDisposable where T : struct
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly ElapsedTimeCounter _counter;

    private bool _disposed;

    private readonly BufferPool<WaveHeaderBuffer> _headerPool;
    private readonly TransformBlock<IntPtr, PcmBuffer<T>> _releaseAction;

    private readonly IDictionary<IntPtr, WaveHeaderBuffer> _headerBusyPool = new ConcurrentDictionary<IntPtr, WaveHeaderBuffer>();
    private readonly IDictionary<IntPtr, PcmBuffer<T>> _dataBusyPool = new ConcurrentDictionary<IntPtr, PcmBuffer<T>>();

    private readonly PinnedDelegate<DriverCallBack.Proc> _driverCallBack;
    private readonly WaveOut _handle;

    private static readonly int SIZE_OF_T = Marshal.SizeOf<T>();
    private static readonly uint SIZE_OF_WAVEHEADER = (uint)Marshal.SizeOf<WaveHeader>();

    private void DriverCallBackProc(
        IntPtr hdrvr,
        DriverCallBack.MM_EXT_WINDOW_MESSAGE uMsg,
        IntPtr dwUser,
        IntPtr dw1,
        IntPtr dw2
    )
    {
        if (uMsg == DriverCallBack.MM_EXT_WINDOW_MESSAGE.WOM_DONE)
        {
#if DEBUG
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug($"[wave] WOM_DONE {dw1}");
                if (_headerBusyPool.TryGetValue(dw1, out var header))
                {
                    var waveHeader = header.Target;
                    _logger.LogDebug(
                        $"[wave] header " +
                        $"data:{waveHeader.data} " +
                        $"bufferLength:{waveHeader.bufferLength} " +
                        $"flags:{waveHeader.flags} " +
                        $"loops:{waveHeader.loops} " +
                        $"user:{waveHeader.user} " +
                        $"next:{waveHeader.next}" +
                        $"reserved:{waveHeader.reserved} "
                    );
                }
            }
#endif
            _releaseAction.Post(dw1);
        }
    }

    internal static WaveFormatExtensiblePart.SPEAKER ToSPEAKER(SPEAKER speaker)
    {
        WaveFormatExtensiblePart.SPEAKER result = 0;
        result |= speaker.HasFlag(SPEAKER.FrontLeft) ? WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT: 0;
        result |= speaker.HasFlag(SPEAKER.FrontRight) ? WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT : 0;
        result |= speaker.HasFlag(SPEAKER.FrontCenter) ? WaveFormatExtensiblePart.SPEAKER.FRONT_CENTER : 0;
        result |= speaker.HasFlag(SPEAKER.LowFrequency) ? WaveFormatExtensiblePart.SPEAKER.LOW_FREQUENCY : 0;
        result |= speaker.HasFlag(SPEAKER.BackLeft) ? WaveFormatExtensiblePart.SPEAKER.BACK_LEFT : 0;
        result |= speaker.HasFlag(SPEAKER.BackRight) ? WaveFormatExtensiblePart.SPEAKER.BACK_RIGHT : 0;
        result |= speaker.HasFlag(SPEAKER.FrontLeftOfCenter) ? WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT_OF_CENTER : 0;
        result |= speaker.HasFlag(SPEAKER.FrontRightOfCenter) ? WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT_OF_CENTER : 0;
        result |= speaker.HasFlag(SPEAKER.BackCenter) ? WaveFormatExtensiblePart.SPEAKER.BACK_CENTER : 0;
        result |= speaker.HasFlag(SPEAKER.SideLeft) ? WaveFormatExtensiblePart.SPEAKER.SIDE_LEFT : 0;
        result |= speaker.HasFlag(SPEAKER.SideRight) ? WaveFormatExtensiblePart.SPEAKER.SIDE_RIGHT : 0;
        result |= speaker.HasFlag(SPEAKER.TopCenter) ? WaveFormatExtensiblePart.SPEAKER.TOP_CENTER : 0;
        result |= speaker.HasFlag(SPEAKER.TopFrontLeft) ? WaveFormatExtensiblePart.SPEAKER.TOP_FRONT_LEFT : 0;
        result |= speaker.HasFlag(SPEAKER.TopFrontCenter) ? WaveFormatExtensiblePart.SPEAKER.TOP_FRONT_CENTER : 0;
        result |= speaker.HasFlag(SPEAKER.TopFrontRight) ? WaveFormatExtensiblePart.SPEAKER.TOP_FRONT_RIGHT : 0;
        result |= speaker.HasFlag(SPEAKER.TopBackLeft) ? WaveFormatExtensiblePart.SPEAKER.TOP_BACK_LEFT : 0;
        result |= speaker.HasFlag(SPEAKER.TopBackCenter) ? WaveFormatExtensiblePart.SPEAKER.TOP_BACK_CENTER : 0;
        result |= speaker.HasFlag(SPEAKER.TopBackRight) ? WaveFormatExtensiblePart.SPEAKER.TOP_BACK_RIGHT : 0;
        return result;
    }

    public WaveOut(
        int deviceID,
        short channels,
        int samplesPerSecond,
        SPEAKER channelMask,
        Guid formatSubType,
        ILoggerFactory loggerFactory,
        ElapsedTimeCounter counter,
        ITargetBlock<PcmBuffer<T>> releaseQueue
    )
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));

        ArgumentNullException.ThrowIfNull(releaseQueue);

        _logger = _loggerFactory.CreateLogger<WaveOut<T>>();
        _headerPool = new BufferPool<WaveHeaderBuffer>(1, () => { return new WaveHeaderBuffer(); }, _loggerFactory);
        _driverCallBack = new PinnedDelegate<DriverCallBack.Proc>(new DriverCallBack.Proc(DriverCallBackProc));

        var format = new WaveFormatExtensible();
        format.wfe.formatType = WaveFormatEx.FORMAT.EXTENSIBLE;
        format.wfe.channels = (ushort)channels;
        format.wfe.samplesPerSecond = (uint)samplesPerSecond;
        format.wfe.bitsPerSample = (ushort)(SIZE_OF_T * 8);
        format.wfe.blockAlign = (ushort)(format.wfe.channels * format.wfe.bitsPerSample / 8);
        format.wfe.averageBytesPerSecond = format.wfe.samplesPerSecond * format.wfe.blockAlign;
        format.wfe.size = (ushort)(Marshal.SizeOf<WaveFormatExtensiblePart>());

        format.exp.validBitsPerSample = format.wfe.bitsPerSample;
        format.exp.channelMask = ToSPEAKER(channelMask);
        format.exp.subFormat = formatSubType;

        //たまに失敗するので、ピン止めしておく
        using var formatPin = new PinnedBuffer<WaveFormatExtensible>(format);

        var mmResult =
            NativeMethods.waveOutOpen(
                out _handle,
                deviceID,
                ref format,
                _driverCallBack.FunctionPointer,
                IntPtr.Zero,
                (
                        DriverCallBack.TYPE.FUNCTION
                    | DriverCallBack.TYPE.WAVE_FORMAT_DIRECT
                )
            );
        if (mmResult != MMRESULT.NOERROR)
        {
            throw new WaveException(mmResult);
        }

        _releaseAction = new TransformBlock<IntPtr, PcmBuffer<T>>(headerPtr => Unprepare(headerPtr));
        _releaseAction.LinkTo(releaseQueue);
    }

    ~WaveOut()
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

        if (_handle != null)
        {
            if (
                !_handle.IsInvalid
                && !_handle.IsClosed
            )
            {
                _logger.LogInformation("[wave] stop");
                Reset();

                //バッファの開放待ち
                _logger.LogInformation($"[wave] wait busy buffers :{_headerBusyPool.Count}");
                while (_headerBusyPool.Count > 0)
                {
                    Thread.Sleep(1000);
                    _logger.LogInformation($"[wave] wait busy buffers :{_headerBusyPool.Count}");
                }
                _logger.LogInformation($"[wave] wait end :{_headerBusyPool.Count}");

                _releaseAction.Complete();
                _releaseAction.Completion.Wait();

                _handle.Close();
            }
        }

        _headerPool?.Dispose();
        _driverCallBack?.Dispose();

        _disposed = true;
    }


    private IntPtr Prepare(PcmBuffer<T> source, CancellationToken ct)
    {
        var header = _headerPool.Receive(ct);
        {
            var waveHeader = header.Target;
            waveHeader.data = source.Buffer.AddrOfPinnedObject;
            waveHeader.bufferLength = (uint)(source.Buffer.Target.Length * SIZE_OF_T);
            waveHeader.flags = 0;
            waveHeader.loops = 1;

            waveHeader.bytesRecorded = 0;
            waveHeader.user = IntPtr.Zero;
            waveHeader.next = IntPtr.Zero;
            waveHeader.reserved = IntPtr.Zero;
        }

        var mmResult =
            _handle.waveOutPrepareHeader(
                header.AddrOfPinnedObject,
                SIZE_OF_WAVEHEADER
            );
        if (mmResult != MMRESULT.NOERROR)
        {
            _headerPool.Post(header);
            throw new WaveException(mmResult);
        }
        _headerBusyPool.Add(header.AddrOfPinnedObject, header);
        _dataBusyPool.Add(source.Buffer.AddrOfPinnedObject, source);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug($"[wave] prepare [{header.AddrOfPinnedObject}] [{_dataBusyPool.Count}]");
        }
        return header.AddrOfPinnedObject;
    }

    private PcmBuffer<T> Unprepare(IntPtr headerPtr)
    {
#pragma warning disable CA2000 // スコープを失う前にオブジェクトを破棄
        if (!_headerBusyPool.Remove(headerPtr, out var header))
#pragma warning restore CA2000 // スコープを失う前にオブジェクトを破棄
        {
            throw new InvalidOperationException("headerBusyPool.Remove failed.");
        }
#if DEBUG
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var waveHeader = header.Target;
            _logger.LogDebug(
                $"[wave] header " +
                $"data:{waveHeader.data} " +
                $"bufferLength:{waveHeader.bufferLength} " +
                $"flags:{waveHeader.flags} " +
                $"loops:{waveHeader.loops} " +
                $"user:{waveHeader.user} " +
                $"next:{waveHeader.next}" +
                $"reserved:{waveHeader.reserved} "
            );
        }
#endif
        var mmResult =
            _handle.waveOutUnprepareHeader(
                headerPtr,
                SIZE_OF_WAVEHEADER
            );
        if (mmResult != MMRESULT.NOERROR)
        {
            throw new WaveException(mmResult);
        }
#if DEBUG
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var waveHeader = header.Target;
            _logger.LogDebug(
                $"[wave] header " +
                $"data:{waveHeader.data} " +
                $"bufferLength:{waveHeader.bufferLength} " +
                $"flags:{waveHeader.flags} " +
                $"loops:{waveHeader.loops} " +
                $"user:{waveHeader.user} " +
                $"next:{waveHeader.next}" +
                $"reserved:{waveHeader.reserved} "
            );
        }
#endif

        if (!_dataBusyPool.Remove(header.Target.data, out var source))
        {
            throw new InvalidOperationException("dataBusyPool.Remove failed.");
        }
        _headerPool.Post(header);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug($"[wave] unprepare [{headerPtr}][{_dataBusyPool.Count}]");
            source.Log.Add("[wave] unprepare", _counter.NowTicks);
            var log = "";
            double? temp = null;
            source.Log.ForEach((a) =>
            {
                var lap = temp.HasValue ? (a.time - temp) : 0;
                log += $"\n[{ new DateTime((long)(a.time * 10), DateTimeKind.Utc):yyyy/MM/dd HH:mm:ss ffffff}][{a.time:0000000000.000}][{lap:0000000000.000}]{a.label}";
                temp = a.time;
            });
            _logger.LogDebug($"[wave] {source.Log.SpentTime()} {log}");
            _logger.LogDebug($"[wave] release [{source.Buffer.AddrOfPinnedObject}]");
        }

        return source;
    }

    private void Send(IntPtr headerPtr)
    {
        var mmResult =
            _handle.waveOutWrite(
                headerPtr,
                SIZE_OF_WAVEHEADER
            );
        if (mmResult != MMRESULT.NOERROR)
        {
            _releaseAction.Post(headerPtr);
            throw new WaveException(mmResult);
        }
    }

    private void Reset()
    {
        var mmResult = _handle.waveOutReset();
        if (mmResult != MMRESULT.NOERROR)
        {
            throw new WaveException(mmResult);
        }
    }

    public void Execute(
        PcmBuffer<T> source,
        CancellationToken ct
    )
    {
        if (
            _handle.IsInvalid
        || _handle.IsClosed
        )
        {
            return;
        }

        if (source == default)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug($"[wave] execute [{source.Buffer.AddrOfPinnedObject}]");
        }
        source.Log.Add("[wave] send start", _counter.NowTicks);
        var headerPtr = Prepare(source, ct);
        Send(headerPtr);
        source.Log.Add("[wave] send end", _counter.NowTicks);
    }
}
