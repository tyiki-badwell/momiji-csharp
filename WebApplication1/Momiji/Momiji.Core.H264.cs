using Microsoft.Extensions.Logging;
using Momiji.Interop;
using Momiji.Interop.H264;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

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
                buffer.Dispose();
            }

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
        public List<FieldInfo> LayerInfoList { get; }

        public SFrameBSInfoBuffer() : base(new SFrameBSInfo())
        {
            LayerInfoList = new List<FieldInfo>();
            for (var idx = 0; idx < 128; idx++)
            {
                LayerInfoList.Add(typeof(SFrameBSInfo).GetField($"sLayerInfo{idx:000}"));
            }
        }
    }

    public class H264Encoder : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private bool disposed = false;
        private SVCEncoder Encoder { get; set; }
        private SFrameBSInfoBuffer sFrameBSInfoBuffer { get; }

        private int PicWidth { get; }
        private int PicHeight { get; }
        private int TargetBitrate { get; }
        private float MaxFrameRate { get; }

        private ISVCEncoderVtbl.InitializeProc Initialize;
        private ISVCEncoderVtbl.GetDefaultParamsProc GetDefaultParams;
        private ISVCEncoderVtbl.UninitializeProc Uninitialize;
        private ISVCEncoderVtbl.EncodeFrameProc EncodeFrame;
        private ISVCEncoderVtbl.EncodeParameterSetsProc EncodeParameterSets;
        private ISVCEncoderVtbl.ForceIntraFrameProc ForceIntraFrame;
        private ISVCEncoderVtbl.SetOptionProc SetOption;
        private ISVCEncoderVtbl.GetOptionProc GetOption;

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

            sFrameBSInfoBuffer = new SFrameBSInfoBuffer();
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
                Logger.LogInformation("[h264] stop");

                if (Encoder != null && !Encoder.IsInvalid)
                {
                    Uninitialize(Encoder);

                    Encoder.Close();
                    Encoder = null;
                }

                if (sFrameBSInfoBuffer != null)
                {
                    sFrameBSInfoBuffer.Dispose();
                }
            }

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
            CopyMemory(
                dest.AddrOfPinnedObject, 
                dest.Target.Length, 
                sFrameBSInfoBuffer.Target.sLayerInfo000.pBsBuf, 
                sFrameBSInfoBuffer.Target.iFrameSizeInBytes
            );

            var sizeOfInt32 = Marshal.SizeOf<Int32>();
            var offset = 0;
            for (var idx = 0; idx < sFrameBSInfoBuffer.Target.iLayerNum; idx++)
            {
                var layer = (SLayerBSInfo)sFrameBSInfoBuffer.LayerInfoList[idx].GetValue(sFrameBSInfoBuffer.Target);

                var nuls = new List<(int offset, int length)>();
                dest.LayerNuls.Add(nuls);
                
                for (var nalIdx = 0; nalIdx < layer.iNalCount; nalIdx++)
                {
                    var length = Marshal.ReadInt32(layer.pNalLengthInByte, nalIdx * sizeOfInt32);
                    nuls.Add((offset + 4, length - 4));
                    offset += length;
                }
            }
        }

        public async Task Run(
            ISourceBlock<H264InputBuffer> sourceQueue,
            ITargetBlock<H264InputBuffer> sourceReleaseQueue,
            ISourceBlock<H264OutputBuffer> destQueue,
            ITargetBlock<H264OutputBuffer> destReleaseQueue,
            CancellationToken ct)
        {
            using (var frameBSInfoBuffer = new PinnedBuffer<SFrameBSInfo>(new SFrameBSInfo()))
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var interval = 1000000.0 / MaxFrameRate;
                    var intraFrameCount = 0.0;

                    while (true)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            break;
                        }

                        var source = sourceQueue.Receive(ct);
                        var dest = destQueue.Receive(ct);

                        Execute(source, dest, (intraFrameCount <= 0));
                        intraFrameCount -= interval;

                        destReleaseQueue.Post(dest);
                        sourceReleaseQueue.Post(source);
                    }
                    Logger.LogInformation("[h264] loop end");
                });
            }
        }

        private void CopyMemory(
            IntPtr Destination,
            long maxLength,
            IntPtr Source,
            long Length)
        {
            if (maxLength < Length)
            {
                throw new H264Exception($"too large {Length} max {maxLength}");
            }

            var temp = new byte[Length];
            Marshal.Copy(Source, temp, 0, (int)Length);
            Marshal.Copy(temp, 0, Destination, (int)Length);
            temp = null;
        }

    }
}