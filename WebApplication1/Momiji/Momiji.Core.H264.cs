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

    public class H264OutputBuffer : PinnedBuffer<byte[]>
    {
        public int Wrote { get; set; }
        public bool EndOfFrame { get; set; }

        public H264OutputBuffer(int size) : base(new byte[size])
        {
        }
    }

    public class H264Encoder : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private bool disposed = false;
        private IntPtr ISVCEncoderVtblPtr { get; set; }

        private int PicWidth { get; }
        private int PicHeight { get; }
        private int TargetBitrate { get; }
        private float MaxFrameRate { get; }
        private double IntraFrameIntervalUs { get; }

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
            int intraFrameIntervalMs,
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
            IntraFrameIntervalUs = intraFrameIntervalMs * 1000;

            {
                IntPtr handle = IntPtr.Zero;
                var result = Encoder.WelsCreateSVCEncoder(out handle);
                if (result != 0)
                {
                    throw new H264Exception($"WelsCreateSVCEncoder failed {result}");
                }
                ISVCEncoderVtblPtr = handle;
            }

            var temp = Marshal.PtrToStructure<IntPtr>(ISVCEncoderVtblPtr);
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

                var result = Initialize(ISVCEncoderVtblPtr, param.AddrOfPinnedObject);
                if (result != 0)
                {
                    throw new H264Exception($"WelsCreateSVCEncoder Initialize failed {result}");
                }
            }
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

                if (ISVCEncoderVtblPtr != IntPtr.Zero)
                {
                    Uninitialize(ISVCEncoderVtblPtr);

                    Encoder.WelsDestroySVCEncoder(ISVCEncoderVtblPtr);
                    ISVCEncoderVtblPtr = IntPtr.Zero;
                }
            }

            disposed = true;
        }

        public async Task Run(
            ISourceBlock<H264OutputBuffer> bufferQueue,
            ITargetBlock<H264OutputBuffer> outputQueue,
            CancellationToken ct)
        {
            var frameSize = PicWidth * PicHeight * 3 / 2;
            using (var buffer = new PinnedBuffer<byte[]>(new byte[frameSize]))
            using (var SSourcePictureBuffer = new PinnedBuffer<SSourcePicture>(new SSourcePicture()))
            using (var SFrameBSInfoBuffer = new PinnedBuffer<SFrameBSInfo>(new SFrameBSInfo()))
            {
                SSourcePictureBuffer.Target.iColorFormat = EVideoFormatType.videoFormatI420;
                SSourcePictureBuffer.Target.iStride0 = PicWidth;
                SSourcePictureBuffer.Target.iStride1 = PicWidth >> 1;
                SSourcePictureBuffer.Target.iStride2 = PicWidth >> 1;
                SSourcePictureBuffer.Target.iStride3 = 0;
                SSourcePictureBuffer.Target.pData0 = buffer.AddrOfPinnedObject;
                SSourcePictureBuffer.Target.pData1 = SSourcePictureBuffer.Target.pData0 + (PicWidth * PicHeight);
                SSourcePictureBuffer.Target.pData2 = SSourcePictureBuffer.Target.pData1 + (PicWidth * PicHeight >> 2);
                SSourcePictureBuffer.Target.pData3 = IntPtr.Zero;
                SSourcePictureBuffer.Target.iPicWidth = PicWidth;
                SSourcePictureBuffer.Target.iPicHeight = PicHeight;

                var layerInfoList = new List<FieldInfo>();
                for (var idx = 0; idx < 128; idx++)
                {
                    layerInfoList.Add(typeof(SFrameBSInfo).GetField($"sLayerInfo{idx:000}"));
                }

                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var before = Timer.USecDouble;
                    var interval = 1000000.0 / MaxFrameRate;
                    var intraFrameCount = 0.0;

                    using (var s = new SemaphoreSlim(1))
                    {
                        while (true)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                break;
                            }

                            //Logger.LogInformation("[h264] get data TRY");

                            {
                                var after = Timer.USecDouble;
                                var diff = after - before;
                                var left = interval - diff;
                                if (left > 0)
                                {
                                    //セマフォで時間調整を行う
                                    s.Wait((int)(left / 1000), ct);
                                    after = Timer.USecDouble;
                                }
                                //Logger.LogInformation($"[h264] start [{diff}+{left}]us [{interval}]us");
                                before = after;
                            }

                            if (intraFrameCount <= 0)
                            {
                                intraFrameCount = IntraFrameIntervalUs;
                                var result = ForceIntraFrame(ISVCEncoderVtblPtr, true);
                                if (result != 0)
                                {
                                    throw new H264Exception($"WelsCreateSVCEncoder ForceIntraFrame failed {result}");
                                }
                            }
                            intraFrameCount -= interval;

                            {
                                SSourcePictureBuffer.Target.uiTimeStamp = (long)(Timer.USecDouble / 1000);
                                var result = EncodeFrame(ISVCEncoderVtblPtr, SSourcePictureBuffer.AddrOfPinnedObject, SFrameBSInfoBuffer.AddrOfPinnedObject);
                                if (result != 0)
                                {
                                    throw new H264Exception($"WelsCreateSVCEncoder EncodeFrame failed {result}");
                                }
                            }

                            for (var idx = 0; idx < SFrameBSInfoBuffer.Target.iLayerNum; idx++)
                            {
                                var layer = (SLayerBSInfo)layerInfoList[idx].GetValue(SFrameBSInfoBuffer.Target);
                                var bsBuf = layer.pBsBuf;

                                for (var nalIdx = 0; nalIdx < layer.iNalCount; nalIdx++)
                                {
                                    var data = bufferQueue.Receive(ct);
                                    var length = Marshal.ReadInt32(layer.pNalLengthInByte, nalIdx * Marshal.SizeOf<Int32>());
                                    CopyMemory(data.AddrOfPinnedObject, bsBuf + 4, length - 4);
                                    bsBuf += length;
                                    data.Wrote = length - 4;
                                    data.EndOfFrame = (nalIdx == layer.iNalCount - 1);
                                    //Logger.LogInformation($"[h264] post data:buffer:{SFrameBSInfoBuffer.Target.eFrameType}, layer:{layer.eFrameType}");
                                    outputQueue.Post(data);
                                }
                            }
                        }
                    }
                    Logger.LogInformation("[h264] loop end");
                });
            }
        }

        private void CopyMemory(
            IntPtr Destination,
            IntPtr Source,
            long Length)
        {
            var temp = new byte[Length];
            Marshal.Copy(Source, temp, 0, (int)Length);
            Marshal.Copy(temp, 0, Destination, (int)Length);
        }

    }
}