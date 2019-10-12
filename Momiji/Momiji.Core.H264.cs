using Microsoft.Extensions.Logging;
using Momiji.Interop;
using Momiji.Interop.H264;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Momiji.Core.H264
{
    public class H264Exception : Exception
    {
        public H264Exception(string message) : base(message)
        {
        }

    }

    public class H264InputBuffer : PinnedBuffer<SSourcePicture>
    {
        private bool disposed = false;
        private PinnedBuffer<byte[]> buffer;

        public H264InputBuffer(int picWidth, int picHeight) : base(new SSourcePicture())
        {
            var frameSize = picWidth * picHeight * 3 / 2;
            buffer = new PinnedBuffer<byte[]>(new byte[frameSize]);

            var target = Target;
            target.iColorFormat = EVideoFormatType.videoFormatI420;
            target.iStride0 = picWidth;
            target.iStride1 = picWidth >> 1;
            target.iStride2 = picWidth >> 1;
            target.iStride3 = 0;
            target.pData0 = buffer.AddrOfPinnedObject;
            target.pData1 = target.pData0 + (picWidth * picHeight);
            target.pData2 = target.pData1 + (picWidth * picHeight >> 2);
            target.pData3 = IntPtr.Zero;
            target.iPicWidth = picWidth;
            target.iPicHeight = picHeight;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposed) return;

            if (disposing)
            {
            }

            buffer.Dispose();

            disposed = true;

            base.Dispose(disposing);
        }
    }

    public class H264OutputBuffer : PinnedBuffer<byte[]>
    {
        public List<List<(int offset, int length)>> LayerNuls { get; }

        public H264OutputBuffer(int size) : base(new byte[size])
        {
            LayerNuls = new List<List<(int, int)>>();
        }
    }

    public class SFrameBSInfoBuffer : PinnedBuffer<SFrameBSInfo>
    {
        public SFrameBSInfoBuffer() : base(new SFrameBSInfo())
        {
        }
    }

    static class SFrameBSInfoBufferExtensions
    {
        static private List<FieldInfo> layerInfoList;

        public static SLayerBSInfo sLayerInfo(this SFrameBSInfoBuffer self, int index)
        {
            if (layerInfoList == null)
            {
                var temp = new List<FieldInfo>();
                for (var idx = 0; idx < 128; idx++)
                {
                    temp.Add(typeof(SFrameBSInfo).GetField($"sLayerInfo{idx:000}"));
                }
                layerInfoList = temp;
            }
            return (SLayerBSInfo)layerInfoList[index].GetValue(self.Target);
        }
    }

    public class H264Encoder : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private bool disposed = false;
        private SVCEncoder Encoder;
        private SFrameBSInfoBuffer sFrameBSInfoBuffer;

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
            Timer timer
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<H264Encoder>();
            Timer = timer;

            PicWidth = picWidth;
            PicHeight = picHeight;
            TargetBitrate = targetBitrate;
            MaxFrameRate = maxFrameRate;

            {
                var result = SVCEncoder.WelsCreateSVCEncoder(out SVCEncoder handle);
                if (result != 0)
                {
                    throw new H264Exception($"WelsCreateSVCEncoder failed {result}");
                }
                Encoder = handle;
            }

            var temp = Marshal.PtrToStructure<IntPtr>(Encoder.DangerousGetHandle());
            var vtbl = Marshal.PtrToStructure<ISVCEncoderVtbl>(temp);
            if (vtbl.Initialize != IntPtr.Zero)
            {
                Initialize =
                    Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.InitializeProc>(vtbl.Initialize);
            }
            if (vtbl.GetDefaultParams != IntPtr.Zero)
            {
                GetDefaultParams =
                    Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.GetDefaultParamsProc>(vtbl.GetDefaultParams);
            }
            if (vtbl.Uninitialize != IntPtr.Zero)
            {
                Uninitialize =
                    Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.UninitializeProc>(vtbl.Uninitialize);
            }
            if (vtbl.EncodeFrame != IntPtr.Zero)
            {
                EncodeFrame =
                    Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.EncodeFrameProc>(vtbl.EncodeFrame);
            }
            if (vtbl.EncodeParameterSets != IntPtr.Zero)
            {
                EncodeParameterSets =
                    Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.EncodeParameterSetsProc>(vtbl.EncodeParameterSets);
            }
            if (vtbl.ForceIntraFrame != IntPtr.Zero)
            {
                ForceIntraFrame =
                    Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.ForceIntraFrameProc>(vtbl.ForceIntraFrame);
            }
            if (vtbl.SetOption != IntPtr.Zero)
            {
                SetOption =
                    Marshal.GetDelegateForFunctionPointer<ISVCEncoderVtbl.SetOptionProc>(vtbl.SetOption);
            }
            if (vtbl.GetOption != IntPtr.Zero)
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

                var result = Initialize(Encoder, param.AddrOfPinnedObject);
                if (result != 0)
                {
                    throw new H264Exception($"WelsCreateSVCEncoder Initialize failed {result}");
                }
            }

            using (var param = new PinnedBuffer<int[]>(new int[1]))
            {
                param.Target[0] = (1 << 5);
            //    SetOption(Encoder, ENCODER_OPTION.ENCODER_OPTION_TRACE_LEVEL, param.AddrOfPinnedObject);
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

            if (Encoder != null)
            {
                if (
                    !Encoder.IsInvalid
                    && !Encoder.IsClosed
                )
                {
                    Uninitialize(Encoder);
                    Encoder.Close();
                }

                Encoder = null;
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
            if (insertIntraFrame)
            {
                source.Log.Add("[h264] ForceIntraFrame", Timer.USecDouble);
                var result = ForceIntraFrame(Encoder, true);
                if (result != 0)
                {
                    throw new H264Exception($"WelsCreateSVCEncoder ForceIntraFrame failed {result}");
                }
            }

            {
                source.Log.Add("[h264] start EncodeFrame", Timer.USecDouble);
                source.Target.uiTimeStamp = (long)(Timer.USecDouble / 1000);
                var result = EncodeFrame(Encoder, source.AddrOfPinnedObject, sFrameBSInfoBuffer.AddrOfPinnedObject);
                if (result != 0)
                {
                    throw new H264Exception($"WelsCreateSVCEncoder EncodeFrame failed {result}");
                }
                source.Log.Add("[h264] end EncodeFrame", Timer.USecDouble);
            }

            dest.Log.Marge(source.Log);
            dest.LayerNuls.Clear();
            dest.Log.Add("[h264] start copy frame", Timer.USecDouble);
            CopyMemory(
                dest.AddrOfPinnedObject, 
                dest.Target.Length, 
                sFrameBSInfoBuffer.Target.sLayerInfo000.pBsBuf, 
                sFrameBSInfoBuffer.Target.iFrameSizeInBytes
            );

            var sizeOfInt32 = Marshal.SizeOf<int>();
            var offset = 0;
            for (var idx = 0; idx < sFrameBSInfoBuffer.Target.iLayerNum; idx++)
            {
                var layer = sFrameBSInfoBuffer.sLayerInfo(idx);

                var nuls = new List<(int offset, int length)>();
                dest.LayerNuls.Add(nuls);
                
                for (var nalIdx = 0; nalIdx < layer.iNalCount; nalIdx++)
                {
                    var length = Marshal.ReadInt32(layer.pNalLengthInByte, nalIdx * sizeOfInt32);
                    nuls.Add((offset + 4, length - 4));
                    offset += length;
                }
            }
            dest.Log.Add("[h264] end copy frame", Timer.USecDouble);
        }

        private unsafe void CopyMemory(
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
}