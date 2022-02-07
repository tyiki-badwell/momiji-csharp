using Microsoft.Extensions.Logging;
using Momiji.Core.Timer;
using Momiji.Interop.Buffer;
using Momiji.Interop.H264;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Momiji.Core.H264;

public class H264Exception : Exception
{
    public H264Exception()
    {
    }

    public H264Exception(string message) : base(message)
    {
    }

    public H264Exception(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class H264InputBuffer : IDisposable
{
    private bool disposed;
    internal PinnedBuffer<SSourcePicture>? SSourcePictureBuffer { get; private set; }
    private PinnedBuffer<byte[]>? dataBuffer;

    public BufferLog Log { get; }

    public H264InputBuffer(int picWidth, int picHeight)
    {
        SSourcePictureBuffer = new(new SSourcePicture());
        Log = new();

        var frameSize = picWidth * picHeight * 3 / 2;
        dataBuffer = new(new byte[frameSize]);

        var target = SSourcePictureBuffer.Target;
        target.iColorFormat = EVideoFormatType.videoFormatI420;
        target.iStride0 = picWidth;
        target.iStride1 = picWidth >> 1;
        target.iStride2 = picWidth >> 1;
        target.iStride3 = 0;
        target.pData0 = dataBuffer.AddrOfPinnedObject;
        target.pData1 = target.pData0 + (picWidth * picHeight);
        target.pData2 = target.pData1 + (picWidth * picHeight >> 2);
        target.pData3 = IntPtr.Zero;
        target.iPicWidth = picWidth;
        target.iPicHeight = picHeight;
    }

    ~H264InputBuffer()
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
        if (disposed)
        {
            return;
        }

        if (disposing)
        {
        }

        dataBuffer?.Dispose();
        dataBuffer = null;
        SSourcePictureBuffer?.Dispose();
        SSourcePictureBuffer = null;

        disposed = true;
    }
}

public class H264OutputBuffer : IDisposable
{
    private bool disposed;
    internal PinnedBuffer<byte[]>? Buffer { get; private set; }

    public BufferLog Log { get; }

    public IList<IList<(int offset, int length)>> LayerNuls { get; }

    public H264OutputBuffer(int size)
    {
        Buffer = new(new byte[size]);
        Log = new();
        LayerNuls = new Collection<IList<(int, int)>>();
    }
    ~H264OutputBuffer()
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
        if (disposed)
        {
            return;
        }

        if (disposing)
        {
        }

        Buffer?.Dispose();
        Buffer = null;

        disposed = true;
    }
}

public class SFrameBSInfoBuffer : PinnedBuffer<SFrameBSInfo>
{
    public SFrameBSInfoBuffer() : base(new SFrameBSInfo())
    {
    }
}

static public class SFrameBSInfoBufferExtensions
{
    private static readonly List<FieldInfo> layerInfoList = InitializeLayerInfoList();

    static List<FieldInfo> InitializeLayerInfoList()
    {
        var temp = new List<FieldInfo>();
        for (var idx = 0; idx < 128; idx++)
        {
            var item = typeof(SFrameBSInfo).GetField($"sLayerInfo{idx:000}");
            if (item == null)
            {
                throw new H264Exception("InitializeLayerInfoList failed.");
            }
            temp.Add(item);
        }
        return temp;
    }

    public static SLayerBSInfo SLayerInfo(this SFrameBSInfoBuffer self, int index)
    {
        ArgumentNullException.ThrowIfNull(self);
        var value = layerInfoList[index].GetValue(self.Target);
        if (value == null)
        {
            throw new H264Exception("SLayerInfo failed.");
        }
        return (SLayerBSInfo)value;
    }
}

public class H264Encoder : IDisposable
{
    private ILoggerFactory LoggerFactory { get; }
    private ILogger Logger { get; }
    private ElapsedTimeCounter Counter { get; }

    private bool disposed;
    private SVCEncoder? encoder;
    private SFrameBSInfoBuffer? sFrameBSInfoBuffer;

    private int PicWidth { get; }
    private int PicHeight { get; }
    private int TargetBitrate { get; }
    private float MaxFrameRate { get; }

    private readonly ISVCEncoderVtbl.InitializeProc Initialize;
    private readonly ISVCEncoderVtbl.GetDefaultParamsProc GetDefaultParams;
    private readonly ISVCEncoderVtbl.UninitializeProc Uninitialize;
    private readonly ISVCEncoderVtbl.EncodeFrameProc EncodeFrame;
    private readonly ISVCEncoderVtbl.EncodeParameterSetsProc EncodeParameterSets;
    private readonly ISVCEncoderVtbl.ForceIntraFrameProc ForceIntraFrame;
    private readonly ISVCEncoderVtbl.SetOptionProc SetOption;
    private readonly ISVCEncoderVtbl.GetOptionProc GetOption;

    public H264Encoder(
        int picWidth,
        int picHeight,
        int targetBitrate,
        float maxFrameRate,
        ILoggerFactory loggerFactory,
        ElapsedTimeCounter counter
    )
    {
        LoggerFactory = loggerFactory;
        Logger = LoggerFactory.CreateLogger<H264Encoder>();
        Counter = counter;

        PicWidth = picWidth;
        PicHeight = picHeight;
        TargetBitrate = targetBitrate;
        MaxFrameRate = maxFrameRate;

        {
            NativeMethods.WelsGetCodecVersionEx(out var version);
            Logger.LogInformation($"[h264] version {version.uMajor}.{version.uMinor}.{version.uReserved}.{version.uRevision}");
        }

        {
            var result = NativeMethods.WelsCreateSVCEncoder(out var handle);
            if (result != 0)
            {
                throw new H264Exception($"WelsCreateSVCEncoder failed {result}");
            }
            encoder = handle;
        }

        var temp = Marshal.PtrToStructure<IntPtr>(encoder.DangerousGetHandle());
        var vtbl = Marshal.PtrToStructure<ISVCEncoderVtbl>(temp);
        //if (vtbl.Initialize != IntPtr.Zero)
        {
            Initialize =
                Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.InitializeProc>(vtbl.Initialize);
        }
        //if (vtbl.GetDefaultParams != IntPtr.Zero)
        {
            GetDefaultParams =
                Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.GetDefaultParamsProc>(vtbl.GetDefaultParams);
        }
        //if (vtbl.Uninitialize != IntPtr.Zero)
        {
            Uninitialize =
                Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.UninitializeProc>(vtbl.Uninitialize);
        }
        //if (vtbl.EncodeFrame != IntPtr.Zero)
        {
            EncodeFrame =
                Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.EncodeFrameProc>(vtbl.EncodeFrame);
        }
        //if (vtbl.EncodeParameterSets != IntPtr.Zero)
        {
            EncodeParameterSets =
                Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.EncodeParameterSetsProc>(vtbl.EncodeParameterSets);
        }
        //if (vtbl.ForceIntraFrame != IntPtr.Zero)
        {
            ForceIntraFrame =
                Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.ForceIntraFrameProc>(vtbl.ForceIntraFrame);
        }
        //if (vtbl.SetOption != IntPtr.Zero)
        {
            SetOption =
                Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.SetOptionProc>(vtbl.SetOption);
        }
        //if (vtbl.GetOption != IntPtr.Zero)
        {
            GetOption =
                Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.GetOptionProc>(vtbl.GetOption);
        }

        using (var param = new PinnedBuffer<SEncParamBase>(new SEncParamBase()))
        {
            param.Target.iUsageType = EUsageType.CAMERA_VIDEO_REAL_TIME;
            param.Target.iPicWidth = PicWidth;
            param.Target.iPicHeight = PicHeight;
            param.Target.iTargetBitrate = TargetBitrate;
            param.Target.iRCMode = RC_MODES.RC_QUALITY_MODE;
            param.Target.fMaxFrameRate = MaxFrameRate;

            var result = Initialize(encoder, param.AddrOfPinnedObject);
            if (result != 0)
            {
                throw new H264Exception($"WelsCreateSVCEncoder Initialize failed {result}");
            }
        }

        //using (var param = new PinnedBuffer<WELS_LOG>(WELS_LOG.WELS_LOG_WARNING))
        using (var param = new PinnedBuffer<int>(1 << 1))
        {
            SetOption(encoder, ENCODER_OPTION.ENCODER_OPTION_TRACE_LEVEL, param.AddrOfPinnedObject);
        }

        /*
        welsTraceCallback = new PinnedDelegate<WelsTraceCallback>(TraceCallBack);
        SetOption(Encoder, ENCODER_OPTION.ENCODER_OPTION_TRACE_CALLBACK, welsTraceCallback.FunctionPointer);
        */
        sFrameBSInfoBuffer = new SFrameBSInfoBuffer();
    }

    ~H264Encoder()
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
        }

        Logger.LogInformation("[h264] stop");

        if (encoder != null)
        {
            if (
                !encoder.IsInvalid
                && !encoder.IsClosed
            )
            {
                Uninitialize(encoder);
                encoder.Close();
            }

            encoder = null;
        }

        sFrameBSInfoBuffer?.Dispose();
        sFrameBSInfoBuffer = null;

        disposed = true;
    }

    public void Execute(
        H264InputBuffer source,
        H264OutputBuffer dest,
        bool insertIntraFrame
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dest);
        if (encoder == default)
        {
            throw new InvalidOperationException("encoder is null.");
        }
        if (sFrameBSInfoBuffer == default)
        {
            throw new InvalidOperationException("sFrameBSInfoBuffer is null.");
        }
        if (source.SSourcePictureBuffer == default)
        {
            throw new InvalidOperationException("source.SSourcePictureBuffer is null.");
        }
        if (dest.Buffer == default)
        {
            throw new InvalidOperationException("dest.Buffer is null.");
        }

        if (insertIntraFrame)
        {
            source.Log.Add("[h264] ForceIntraFrame", Counter.NowTicks);
            var result = ForceIntraFrame(encoder, true);
            if (result != 0)
            {
                throw new H264Exception($"WelsCreateSVCEncoder ForceIntraFrame failed {result}");
            }
        }

        {
            source.Log.Add("[h264] start EncodeFrame", Counter.NowTicks);
            source.SSourcePictureBuffer.Target.uiTimeStamp = Counter.NowTicks / 10000;
            var result = EncodeFrame(encoder, source.SSourcePictureBuffer.AddrOfPinnedObject, sFrameBSInfoBuffer.AddrOfPinnedObject);
            if (result != 0)
            {
                throw new H264Exception($"WelsCreateSVCEncoder EncodeFrame failed {result}");
            }
            source.Log.Add("[h264] end EncodeFrame", Counter.NowTicks);
        }

        dest.Log.Marge(source.Log);
        dest.LayerNuls.Clear();
        dest.Log.Add("[h264] start copy frame", Counter.NowTicks);
        CopyMemory(
            dest.Buffer.AddrOfPinnedObject,
            dest.Buffer.Target.Length,
            sFrameBSInfoBuffer.Target.sLayerInfo000.pBsBuf,
            sFrameBSInfoBuffer.Target.iFrameSizeInBytes
        );

        var sizeOfInt32 = Marshal.SizeOf<int>();
        var offset = 0;
        for (var idx = 0; idx < sFrameBSInfoBuffer.Target.iLayerNum; idx++)
        {
            var layer = sFrameBSInfoBuffer.SLayerInfo(idx);
            var nuls = new Collection<(int offset, int length)>();
            dest.LayerNuls.Add(nuls);

            for (var nalIdx = 0; nalIdx < layer.iNalCount; nalIdx++)
            {
                //TODO spanにしてみる
                var length = Marshal.ReadInt32(layer.pNalLengthInByte, nalIdx * sizeOfInt32);
                nuls.Add((offset + 4, length - 4));
                offset += length;
            }
        }
        dest.Log.Add("[h264] end copy frame", Counter.NowTicks);
    }

    private unsafe static void CopyMemory(
        IntPtr Destination,
        int maxLength,
        IntPtr Source,
        int Length)
    {
        if (maxLength < Length)
        {
            throw new H264Exception($"too large {Length} max {maxLength}");
        }
        var destSpan = new Span<byte>((byte*)Destination, maxLength);
        var sourceSpan = new ReadOnlySpan<byte>((byte*)Source, Length);
        sourceSpan.CopyTo(destSpan);
    }

}
