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

            Target.iColorFormat = EVideoFormatType.videoFormatI420;
            Target.iStride0 = picWidth;
            Target.iStride1 = picWidth >> 1;
            Target.iStride2 = picWidth >> 1;
            Target.iStride3 = 0;
            Target.pData0 = buffer.AddrOfPinnedObject;
            Target.pData1 = Target.pData0 + (picWidth * picHeight);
            Target.pData2 = Target.pData1 + (picWidth * picHeight >> 2);
            Target.pData3 = IntPtr.Zero;
            Target.iPicWidth = picWidth;
            Target.iPicHeight = picHeight;
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
        private Encoder encoder { get; set; }

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
                Encoder handle = null;
                var result = Encoder.WelsCreateSVCEncoder(out handle);
                if (result != 0)
                {
                    throw new H264Exception($"WelsCreateSVCEncoder failed {result}");
                }
                encoder = handle;
            }

            var temp = Marshal.PtrToStructure<IntPtr>(encoder.DangerousGetHandle());
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

                var result = Initialize(encoder, param.AddrOfPinnedObject);
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

                if (encoder != null && !encoder.IsInvalid)
                {
                    Uninitialize(encoder);

                    encoder.Close();
                    encoder = null;
                }
            }

            disposed = true;
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
                var layerInfoList = new List<FieldInfo>();
                for (var idx = 0; idx < 128; idx++)
                {
                    layerInfoList.Add(typeof(SFrameBSInfo).GetField($"sLayerInfo{idx:000}"));
                }

                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var interval = 1000000.0 / MaxFrameRate;
                    var intraFrameCount = 0.0;

                    using (var w = new Waiter(Timer, interval, ct))
                    {
                        while (true)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                break;
                            }

                            //Logger.LogInformation("[h264] get data TRY");

                            List<Tuple<string, double>> log;
                            {
                                var source = sourceQueue.Receive(ct);
                                w.Wait();

                                if (intraFrameCount <= 0)
                                {
                                    intraFrameCount = IntraFrameIntervalUs;
                                    var result = ForceIntraFrame(encoder, true);
                                    if (result != 0)
                                    {
                                        throw new H264Exception($"WelsCreateSVCEncoder ForceIntraFrame failed {result}");
                                    }
                                }
                                intraFrameCount -= interval;

                                {
                                    source.Log.Add("[h264] start EncodeFrame", Timer.USecDouble);
                                    source.Target.uiTimeStamp = (long)(Timer.USecDouble / 1000);
                                    var result = EncodeFrame(encoder, source.AddrOfPinnedObject, frameBSInfoBuffer.AddrOfPinnedObject);
                                    if (result != 0)
                                    {
                                        throw new H264Exception($"WelsCreateSVCEncoder EncodeFrame failed {result}");
                                    }
                                    source.Log.Add("[h264] end EncodeFrame", Timer.USecDouble);
                                }
                                log = source.Log.Copy();
                                sourceReleaseQueue.SendAsync(source);
                            }

                            for (var idx = 0; idx < frameBSInfoBuffer.Target.iLayerNum; idx++)
                            {
                                var layer = (SLayerBSInfo)layerInfoList[idx].GetValue(frameBSInfoBuffer.Target);
                                var bsBuf = layer.pBsBuf;

                                for (var nalIdx = 0; nalIdx < layer.iNalCount; nalIdx++)
                                {
                                    var dest = destQueue.Receive(ct);
                                    dest.Log.Marge(log);
                                    var length = Marshal.ReadInt32(layer.pNalLengthInByte, nalIdx * Marshal.SizeOf<Int32>());
                                    CopyMemory(dest.AddrOfPinnedObject, bsBuf + 4, length - 4);
                                    bsBuf += length;
                                    dest.Wrote = length - 4;
                                    dest.EndOfFrame = (nalIdx == layer.iNalCount - 1);

                                    dest.Log.Add("[h264] nal", Timer.USecDouble);
                                    destReleaseQueue.SendAsync(dest);
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