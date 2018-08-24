using Momiji.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private bool disposed = false;
        public IntPtr ISVCEncoderVtblPtr { get; private set; }

        private int picWidth;
        private int picHeight;

        private Interop.H264.ISVCEncoderVtbl.InitializeProc Initialize;
        private Interop.H264.ISVCEncoderVtbl.GetDefaultParamsProc GetDefaultParams;
        private Interop.H264.ISVCEncoderVtbl.UninitializeProc Uninitialize;
        private Interop.H264.ISVCEncoderVtbl.EncodeFrameProc EncodeFrame;
        private Interop.H264.ISVCEncoderVtbl.EncodeParameterSetsProc EncodeParameterSets;
        private Interop.H264.ISVCEncoderVtbl.ForceIntraFrameProc ForceIntraFrame;
        private Interop.H264.ISVCEncoderVtbl.SetOptionProc SetOption;
        private Interop.H264.ISVCEncoderVtbl.GetOptionProc GetOption;

        public H264Encoder(
            int picWidth,
            int picHeight,
            int targetBitrate,
            float maxFrameRate
        )
        {
            this.picWidth = picWidth;
            this.picHeight = picHeight;
            {
                IntPtr handle = IntPtr.Zero;
                var result = Interop.H264.WelsCreateSVCEncoder(out handle);
                if (result != 0)
                {
                    throw new H264Exception($"WelsCreateSVCEncoder failed {result}");
                }
                ISVCEncoderVtblPtr = handle;
            }

            var temp = Marshal.PtrToStructure<IntPtr>(ISVCEncoderVtblPtr);
            var vtbl = Marshal.PtrToStructure<Interop.H264.ISVCEncoderVtbl>(temp);
            if (vtbl.Initialize != IntPtr.Zero)
            {
                Initialize =
                    Marshal.GetDelegateForFunctionPointer<Interop.H264.ISVCEncoderVtbl.InitializeProc>(vtbl.Initialize);
            }
            if (vtbl.GetDefaultParams != IntPtr.Zero)
            {
                GetDefaultParams =
                    Marshal.GetDelegateForFunctionPointer<Interop.H264.ISVCEncoderVtbl.GetDefaultParamsProc>(vtbl.GetDefaultParams);
            }
            if (vtbl.Uninitialize != IntPtr.Zero)
            {
                Uninitialize =
                    Marshal.GetDelegateForFunctionPointer<Interop.H264.ISVCEncoderVtbl.UninitializeProc>(vtbl.Uninitialize);
            }
            if (vtbl.EncodeFrame != IntPtr.Zero)
            {
                EncodeFrame =
                    Marshal.GetDelegateForFunctionPointer<Interop.H264.ISVCEncoderVtbl.EncodeFrameProc>(vtbl.EncodeFrame);
            }
            if (vtbl.EncodeParameterSets != IntPtr.Zero)
            {
                EncodeParameterSets =
                    Marshal.GetDelegateForFunctionPointer<Interop.H264.ISVCEncoderVtbl.EncodeParameterSetsProc>(vtbl.EncodeParameterSets);
            }
            if (vtbl.ForceIntraFrame != IntPtr.Zero)
            {
                ForceIntraFrame =
                    Marshal.GetDelegateForFunctionPointer<Interop.H264.ISVCEncoderVtbl.ForceIntraFrameProc>(vtbl.ForceIntraFrame);
            }
            if (vtbl.SetOption != IntPtr.Zero)
            {
                SetOption =
                    Marshal.GetDelegateForFunctionPointer<Interop.H264.ISVCEncoderVtbl.SetOptionProc>(vtbl.SetOption);
            }
            if (vtbl.GetOption != IntPtr.Zero)
            {
                GetOption =
                    Marshal.GetDelegateForFunctionPointer<Interop.H264.ISVCEncoderVtbl.GetOptionProc>(vtbl.GetOption);
            }

            using (var param = new PinnedBuffer<Interop.H264.SEncParamBase>(new Interop.H264.SEncParamBase()))
            {
                param.Target.iUsageType = Interop.H264.EUsageType.CAMERA_VIDEO_REAL_TIME;
                param.Target.iPicWidth = picWidth;
                param.Target.iPicHeight = picHeight;
                param.Target.iTargetBitrate = targetBitrate;
                param.Target.iRCMode = Interop.H264.RC_MODES.RC_QUALITY_MODE;
                param.Target.fMaxFrameRate = maxFrameRate;

                var result = Initialize(ISVCEncoderVtblPtr, param.AddrOfPinnedObject());
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
                Trace.WriteLine("[h264] stop");

                if (ISVCEncoderVtblPtr != IntPtr.Zero)
                {
                    Uninitialize(ISVCEncoderVtblPtr);

                    Interop.H264.WelsDestroySVCEncoder(ISVCEncoderVtblPtr);
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
            var frameSize = picWidth * picHeight * 3 / 2;
            using (var buffer = new PinnedBuffer<byte[]>(new byte[frameSize]))
            using (var SSourcePictureBuffer = new PinnedBuffer<Interop.H264.SSourcePicture>(new Interop.H264.SSourcePicture()))
            using (var SFrameBSInfoBuffer = new PinnedBuffer<Interop.H264.SFrameBSInfo>(new Interop.H264.SFrameBSInfo()))
            {
                SSourcePictureBuffer.Target.iColorFormat = Interop.H264.EVideoFormatType.videoFormatI420;
                SSourcePictureBuffer.Target.iStride0 = picWidth;
                SSourcePictureBuffer.Target.iStride1 = picWidth >> 1;
                SSourcePictureBuffer.Target.iStride2 = picWidth >> 1;
                SSourcePictureBuffer.Target.iStride3 = 0;
                SSourcePictureBuffer.Target.pData0 = buffer.AddrOfPinnedObject();
                SSourcePictureBuffer.Target.pData1 = SSourcePictureBuffer.Target.pData0 + (picWidth * picHeight);
                SSourcePictureBuffer.Target.pData2 = SSourcePictureBuffer.Target.pData1 + (picWidth * picHeight >> 2);
                SSourcePictureBuffer.Target.pData3 = IntPtr.Zero;
                SSourcePictureBuffer.Target.iPicWidth = picWidth;
                SSourcePictureBuffer.Target.iPicHeight = picHeight;

                var layerInfoList = new List<FieldInfo>();
                for (var idx = 0; idx < 128; idx++)
                {
                    layerInfoList.Add(typeof(Interop.H264.SFrameBSInfo).GetField($"sLayerInfo{idx:000}"));
                }

                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var stopwatch = Stopwatch.StartNew();
                    var before = stopwatch.ElapsedMilliseconds;
                    var interval = 33.33333;

                    using (var s = new SemaphoreSlim(1))
                    {
                        while (true)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                break;
                            }

                            try
                            {
                                //Trace.WriteLine("[h264] get data TRY");
                                
                                //var data = bufferQueue.Receive(new TimeSpan(20_000_000), ct);
                                {
                                    var after = stopwatch.ElapsedMilliseconds;
                                    var diff = after - before;
                                    var left = interval - diff;
                                    if (left > 0)
                                    {
                                        //セマフォで時間調整を行う
                                        s.Wait((int)left, ct);
                                        after = stopwatch.ElapsedMilliseconds;
                                    }
                                    //    Trace.WriteLine($"[h264] get data OK [{diff}+{left}]ms [{interval}]ms ");
                                    before = after;
                                }

                                SSourcePictureBuffer.Target.uiTimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                {
                                    var result = ForceIntraFrame(ISVCEncoderVtblPtr, true);
                                    if (result != 0)
                                    {
                                        throw new H264Exception($"WelsCreateSVCEncoder ForceIntraFrame failed {result}");
                                    }
                                }
                                {
                                    var result = EncodeFrame(ISVCEncoderVtblPtr, SSourcePictureBuffer.AddrOfPinnedObject(), SFrameBSInfoBuffer.AddrOfPinnedObject());
                                    if (result != 0)
                                    {
                                        throw new H264Exception($"WelsCreateSVCEncoder EncodeFrame failed {result}");
                                    }
                                }
                                /*
                                data.Wrote = 0;
                                var dataPtr = data.AddrOfPinnedObject();
                                */
                                for (var idx = 0; idx < SFrameBSInfoBuffer.Target.iLayerNum; idx++)
                                {
                                    var layer = (Interop.H264.SLayerBSInfo)layerInfoList[idx].GetValue(SFrameBSInfoBuffer.Target);
                                    for (var nalIdx =0; nalIdx < layer.iNalCount; nalIdx++)
                                    {
                                        var data = bufferQueue.Receive(new TimeSpan(20_000_000), ct);
                                        var length = Marshal.ReadInt32(layer.pNalLengthInByte, nalIdx * Marshal.SizeOf<Int32>());
                                        Kernel32.RtlCopyMemory(data.AddrOfPinnedObject(), layer.pBsBuf+4, length-4);
                                        data.Wrote = length-4;
                                        data.EndOfFrame = (nalIdx == layer.iNalCount-1);
                                        outputQueue.Post(data);
                                    }
                                }
                                var finish = stopwatch.ElapsedMilliseconds;
                                //    Trace.WriteLine($"[h264] post data:[{finish - before}]ms");
                            }
                            catch (TimeoutException te)
                            {
                                Trace.WriteLine("[h264] timeout");
                                continue;
                            }
                        }
                    }
                    Trace.WriteLine("[h264] loop end");
                });
            }
        }

    }
}