using Microsoft.Extensions.Logging;
using Momiji.Core.SharedMemory;
using Momiji.Core.Timer;
using Momiji.Core.WebMidi;
using Momiji.Core.Window;
using Momiji.Interop.Buffer;
using Momiji.Interop.Kernel32;
using Momiji.Interop.Vst;
using Momiji.Interop.Vst.AudioMaster;
using Momiji.Interop.Windows.Graphics.Capture;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.Vst
{

    public class VstException : Exception
    {
        public VstException()
        {
        }

        public VstException(string message) : base(message)
        {
        }

        public VstException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public class VstBuffer<T> : IDisposable where T : struct
    {
        private bool disposed;
        internal PinnedBuffer<IntPtr[]> Buffer { get; }
        private readonly List<PinnedBuffer<T[]>> list = new();
        public int BlockSize { get; }

        public BufferLog Log { get; }

        public VstBuffer(int blockSize, int channels)
        {
            if (blockSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(blockSize), blockSize, "1以上にしてください");
            }

            if (channels < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(channels), channels, "1以上にしてください");
            }

            Buffer = new(new IntPtr[channels]);
            Log = new();
            BlockSize = blockSize;
            for (var i = 0; i < channels; i++)
            {
                var buffer = new PinnedBuffer<T[]>(new T[blockSize]);
                list.Add(buffer);
                Buffer.Target[i] = buffer.AddrOfPinnedObject;
            }
        }

        ~VstBuffer()
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
                list.ForEach(buffer => buffer.Dispose());
                list.Clear();
            }

            Buffer?.Dispose();
            disposed = true;
        }

        public T[] GetChannelBuffer(int channel) => list[channel].Target;
    }

    public class VstBuffer2<T> : IDisposable where T : struct
    {
        private bool disposed;

        internal PinnedBuffer<IntPtr[]> Buffer { get; }
        public int BlockSize { get; }

        public BufferLog Log { get; }
        public VstBuffer2(int blockSize, int channels, IPCBuffer<T> buf)
        {
            if (blockSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(blockSize), blockSize, "1以上にしてください");
            }

            if (channels < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(channels), channels, "1以上にしてください");
            }

            if (buf == default)
            {
                throw new ArgumentNullException(nameof(buf));
            }

            Buffer = new(new IntPtr[channels]);
            Log = new();
            BlockSize = blockSize;
            for (var i = 0; i < channels; i++)
            {
                Buffer.Target[i] = buf.Allocate(blockSize);
            }
        }

        ~VstBuffer2()
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

            Buffer?.Dispose();
            disposed = true;
        }
        public IntPtr GetChannelBuffer(int channel) => Buffer.Target[channel];
    }

    public interface IEffect<T> where T : struct
    {
        void ProcessReplacing(
            double nowTime,
            VstBuffer<T> source
        );

        void ProcessReplacing(
            double nowTime,
            VstBuffer2<T> source
        );
        void ProcessEvent(
            double nowTime,
            IReceivableSourceBlock<MIDIMessageEvent2> midiEventInput,
            ITargetBlock<MIDIMessageEvent2> midiEventOutput = null
        );
        void OpenEditor(CancellationToken cancellationToken);
        Task CloseEditor();
    }

    internal class Effect<T> : IEffect<T>, IDisposable where T : struct
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private LapTimer LapTimer { get; }

        private bool disposed;

        internal readonly IntPtr aeffectPtr;

        private NativeWindow window;
        private Task windowTask;

        //private Bitmap bitmap;
        internal ERect EditorRect { get; private set; }

        private AEffect.DispatcherProc DispatcherProc { get; set; }
        private AEffect.SetParameterProc SetParameterProc { get; set; }
        private AEffect.GetParameterProc GetParameterProc { get; set; }
        private AEffect.ProcessProc ProcessProc { get; set; }

        private readonly AudioMaster<T> audioMaster;
        private PinnedDelegate<CallBack> audioMasterCallBack;

        private PinnedBuffer<byte[]> events;
        private PinnedBuffer<byte[]> eventList;

        private double beforeTime;
        private MIDIMessageEvent2? extraMidiEvent;

        private static readonly int SIZE_OF_VSTEVENTS = Marshal.SizeOf<VstEvents>();
        private static readonly int SIZE_OF_VSTMIDIEVENT = Marshal.SizeOf<VstMidiEvent>();
        private static readonly int SIZE_OF_INTPTR = Marshal.SizeOf<IntPtr>();
        private static readonly int SIZE_OF_T = Marshal.SizeOf<T>();
        private static readonly int COUNT_OF_EVENTS = 500; //サイズが適当

        internal Effect(
            string library,
            AudioMaster<T> audioMaster,
            ILoggerFactory loggerFactory,
            LapTimer lapTimer
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Effect<T>>();
            LapTimer = lapTimer;

            this.audioMaster = audioMaster;

            //TODO その場で作っても問題ないか？
            events = new(new byte[SIZE_OF_VSTEVENTS + (SIZE_OF_INTPTR * COUNT_OF_EVENTS)]);
            eventList = new(new byte[SIZE_OF_VSTMIDIEVENT * COUNT_OF_EVENTS]);

            var vstPluginMain = audioMaster.DllManager.GetExport<AEffect.VSTPluginMain>(library, "VSTPluginMain");
            if (vstPluginMain == default)
            {
                vstPluginMain = audioMaster.DllManager.GetExport<AEffect.VSTPluginMain>(library, "main");
            }

            if (vstPluginMain == default)
            {
                throw new VstException("VSTPluginMainが取得できない");
            }

            audioMasterCallBack = new(new(audioMaster.AudioMasterCallBackProc));
            aeffectPtr = vstPluginMain(audioMasterCallBack.FunctionPointer);
            if (aeffectPtr == default)
            {
                throw new VstException("vstPluginMain で失敗");
            }

            var aeffect = GetAEffect();

            Logger.LogInformation($"magic:{aeffect.magic}");
            Logger.LogInformation($"dispatcher:{aeffect.dispatcher}");
            Logger.LogInformation($"processDeprecated:{aeffect.processDeprecated}");
            Logger.LogInformation($"setParameter:{aeffect.setParameter}");
            Logger.LogInformation($"getParameter:{aeffect.getParameter}");

            Logger.LogInformation($"numPrograms:{aeffect.numPrograms}");
            Logger.LogInformation($"numParams:{aeffect.numParams}");
            Logger.LogInformation($"numInputs:{aeffect.numInputs}");
            Logger.LogInformation($"numOutputs:{aeffect.numOutputs}");
            Logger.LogInformation($"flags:{aeffect.flags}");

            //Logger.LogInformation($"resvd1:"+aeffect.resvd1);
            //Logger.LogInformation($"resvd2:"+aeffect.resvd2);

            Logger.LogInformation($"initialDelay:{aeffect.initialDelay}");

            Logger.LogInformation($"realQualitiesDeprecated:{aeffect.realQualitiesDeprecated}");
            Logger.LogInformation($"offQualitiesDeprecated:{aeffect.offQualitiesDeprecated}");
            Logger.LogInformation($"ioRatioDeprecated:{aeffect.ioRatioDeprecated}");
            //Logger.LogInformation($"object:"+aeffect._object);
            Logger.LogInformation($"user:{aeffect.user}");

            Logger.LogInformation($"uniqueID:{aeffect.uniqueID}");
            Logger.LogInformation($"version:{aeffect.version}");

            Logger.LogInformation("processReplacing:" + aeffect.processReplacing);
            Logger.LogInformation("processDoubleReplacing:" + aeffect.processDoubleReplacing);

            if (aeffect.dispatcher != default)
            {
                DispatcherProc =
                    Marshal.GetDelegateForFunctionPointer<AEffect.DispatcherProc>(aeffect.dispatcher);
            }

            if (aeffect.setParameter != default)
            {
                SetParameterProc =
                    Marshal.GetDelegateForFunctionPointer<AEffect.SetParameterProc>(aeffect.setParameter);
            }

            if (aeffect.getParameter != default)
            {
                GetParameterProc =
                    Marshal.GetDelegateForFunctionPointer<AEffect.GetParameterProc>(aeffect.getParameter);
            }

            if (SIZE_OF_T == 4)
            {
                if (aeffect.processReplacing != default)
                {
                    ProcessProc =
                        Marshal.GetDelegateForFunctionPointer<AEffect.ProcessProc>(aeffect.processReplacing);
                }
                else
                {
                    throw new VstException("processReplacing が無い");
                }
            }
            else
            {
                if (aeffect.processDoubleReplacing != default)
                {
                    ProcessProc =
                        Marshal.GetDelegateForFunctionPointer<AEffect.ProcessProc>(aeffect.processDoubleReplacing);
                }
                else
                {
                    throw new VstException("processDoubleReplacing が無い");
                }
            }

            beforeTime = LapTimer.USecDouble;
        }

        ~Effect()
        {
            Dispose(false);
        }

        internal ref AEffect GetAEffect()
        {
            unsafe
            {
                return ref Unsafe.AsRef<AEffect>((void*)aeffectPtr);
            }
        }

        public float GetParameter(int index)
        {
            if (GetParameterProc == default)
            {
                return default;
            }
            return GetParameterProc(aeffectPtr, index);
        }

        public void SetParameter(int index, float value)
        {
            if (SetParameterProc == default)
            {
                return;
            }
            SetParameterProc(aeffectPtr, index, value);
        }

        private IntPtr Dispatcher(
            AEffect.Opcodes opcode,
            int index,
            IntPtr value,
            IntPtr ptr,
            float opt
        )
        {
            if (DispatcherProc == default)
            {
                return default;
            }

            Logger.LogInformation($"[vst] DispatcherProc({opcode},{index},{value},{ptr},{opt})");
            try
            {
                return DispatcherProc(
                    aeffectPtr,
                    opcode,
                    index,
                    value,
                    ptr,
                    opt
                );
            }
#pragma warning disable CA1031 // 一般的な例外の種類はキャッチしません
            catch (Exception e)
#pragma warning restore CA1031 // 一般的な例外の種類はキャッチしません
            {
                Logger.LogError(e, $"[vst] DispatcherProc Exception({opcode},{index},{value},{ptr},{opt})");
                return IntPtr.Zero;
            }
        }

        private string GetString(AEffect.Opcodes opcode, int index, int length)
        {
            using var buffer = new PinnedBuffer<byte[]>(new byte[length + 1]);

            Dispatcher(
                opcode,
                index,
                default,
                buffer.AddrOfPinnedObject,
                default
            );

            return Marshal.PtrToStringAnsi(buffer.AddrOfPinnedObject);
        }
        public string GetParameterLabel(int index)
        {
            return GetString(AEffect.Opcodes.effGetParamLabel, index, 100);
        }
        public string GetParameterName(int index)
        {
            return GetString(AEffect.Opcodes.effGetParamName, index, 100);
        }
        public string GetParameterDisplay(int index)
        {
            return GetString(AEffect.Opcodes.effGetParamDisplay, index, 100);
        }

        public void ProcessReplacing(
            double nowTime,
            VstBuffer<T> source
        )
        {
            if (ProcessProc == default)
            {
                return;
            }
            if (source == default)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var blockSize = source.BlockSize;

            source.Log.Add("[vst] start processReplacing", LapTimer.USecDouble);
            try
            {
                ProcessProc(
                    aeffectPtr,
                    default,
                    source.Buffer.AddrOfPinnedObject,
                    blockSize
                );
                source.Log.Add("[vst] end processReplacing", LapTimer.USecDouble);
                beforeTime = nowTime;
            }
#pragma warning disable CA1031 // 一般的な例外の種類はキャッチしません
            catch (Exception e)
#pragma warning restore CA1031 // 一般的な例外の種類はキャッチしません
            {
                Logger.LogError(e, $"[vst] ProcessProc Exception");
            }
        }

        public void ProcessReplacing(
            double nowTime,
            VstBuffer2<T> source
        )
        {
            if (ProcessProc == default)
            {
                return;
            }
            if (source == default)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var blockSize = source.BlockSize;

            source.Log.Add("[vst] start processReplacing", LapTimer.USecDouble);
            try
            {
                ProcessProc(
                    aeffectPtr,
                    default,
                    source.Buffer.AddrOfPinnedObject,
                    blockSize
                );
                source.Log.Add("[vst] end processReplacing", LapTimer.USecDouble);
                beforeTime = nowTime;
            }
#pragma warning disable CA1031 // 一般的な例外の種類はキャッチしません
            catch (Exception e)
#pragma warning restore CA1031 // 一般的な例外の種類はキャッチしません
            {
                Logger.LogError(e, $"[vst] ProcessProc Exception");
            }
        }

        public void ProcessEvent(
            double nowTime,
            IReceivableSourceBlock<MIDIMessageEvent2> midiEventInput,
            ITargetBlock<MIDIMessageEvent2> midiEventOutput = null
        )
        {
            var samplingRate = audioMaster.SamplingRate;

            var list = new List<MIDIMessageEvent2>();
            if (extraMidiEvent.HasValue)
            {
                //前回の余分なイベントをここで回収
                list.Add(extraMidiEvent.Value);
                midiEventOutput?.Post(extraMidiEvent.Value);
                extraMidiEvent = null;
            }

            {
                //TODO この一帯がかなり遅い
                while (midiEventInput.TryReceive(out MIDIMessageEvent2 item))
                {
                    if ((item.midiMessageEvent.receivedTime * 1000) > nowTime)
                    {
                        //処理している間にもイベントが増えているので、取りすぎたら次回に回す
                        extraMidiEvent = item;
                        break;
                    }
                    list.Add(item);
                    midiEventOutput?.Post(item);
                }
            }

            if (list.Count > 0)
            {
                var eventsPtr = events.AddrOfPinnedObject;
                var eventListPtr = eventList.AddrOfPinnedObject;

                var vstEvents = new VstEvents
                {
                    numEvents = list.Count
                };

                //TODO 境界チェック
                Marshal.StructureToPtr(vstEvents, eventsPtr, false);
                eventsPtr += SIZE_OF_VSTEVENTS;

                list.ForEach(midiEvent =>
                {
                    //TODO 遅延の平均を足す？
                    var deltaTime = (midiEvent.midiMessageEvent.receivedTime * 1000) - beforeTime;
                    if (deltaTime < 0)
                    {
                        deltaTime = 0;
                    }
                    var vstEvent = new VstMidiEvent
                    {
                        type = VstEvent.VstEventTypes.kVstMidiType,
                        byteSize = SIZE_OF_VSTMIDIEVENT,
                        deltaFrames = (int)(deltaTime * samplingRate / 1000000),
                        flags = VstMidiEvent.VstMidiEventFlags.kVstMidiEventIsRealtime,

                        midiData0 = midiEvent.midiMessageEvent.data0,
                        midiData1 = midiEvent.midiMessageEvent.data1,
                        midiData2 = midiEvent.midiMessageEvent.data2,
                        midiData3 = midiEvent.midiMessageEvent.data3
                    };

                    //TODO 境界チェック
                    Marshal.StructureToPtr(vstEvent, eventListPtr, false);
                    Marshal.WriteIntPtr(eventsPtr, eventListPtr);
                    eventListPtr += SIZE_OF_VSTMIDIEVENT;
                    eventsPtr += SIZE_OF_INTPTR;
                });

                Dispatcher(
                    AEffect.Opcodes.effProcessEvents,
                    default,
                    default,
                    events.AddrOfPinnedObject,
                    default
                );
            }
        }
        internal void Open()
        {
            var aeffect = GetAEffect();
            if (!aeffect.flags.HasFlag(AEffect.VstAEffectFlags.effFlagsIsSynth))
            {
                throw new VstException("effFlagsIsSynth ではない");
            }

            // open
            Dispatcher(
                AEffect.Opcodes.effOpen,
                default,
                default,
                default,
                default
            );

            // set sampling rate
            Dispatcher(
                AEffect.Opcodes.effSetSampleRate,
                default,
                default,
                default,
                audioMaster.SamplingRate
            );

            // set block size
            Dispatcher(
                AEffect.Opcodes.effSetBlockSize,
                default,
                new IntPtr(audioMaster.BlockSize),
                default,
                default
            );

            // set process precision
            Dispatcher(
                AEffect.Opcodes.effSetProcessPrecision,
                default,
                new IntPtr((int)((SIZE_OF_T == 8) ? VstProcessPrecision.kVstProcessPrecision64 : VstProcessPrecision.kVstProcessPrecision32)),
                default,
                default
            );

            // resume
            Dispatcher(
                AEffect.Opcodes.effMainsChanged,
                default,
                new IntPtr(1),
                default,
                default
            );

            // start
            Dispatcher(
                AEffect.Opcodes.effStartProcess,
                default,
                default,
                default,
                default
            );
        }

        public void OpenEditor(CancellationToken cancellationToken)
        {
            var aeffect = GetAEffect();
            if (!aeffect.flags.HasFlag(AEffect.VstAEffectFlags.effFlagsHasEditor))
            {
                Logger.LogInformation("[vst] effFlagsHasEditorではない");
                return;
            }

            if (window != default)
            {
                Logger.LogInformation("[vst] Editorが起動済");
                return;
            }

            window = new NativeWindow(LoggerFactory, OnCreateWindow, OnPreCloseWindow, OnPostPaint);

            windowTask = window.RunAsync(cancellationToken);
            Logger.LogInformation("[vst] Editor Open");
            _ = windowTask.ContinueWith((task) =>
            {
                window = default;
                windowTask = default;
            }, TaskScheduler.Default).ConfigureAwait(false);
        }

        private void OnCreateWindow(HandleRef hWindow, ref int width, ref int height)
        {
            {
                Logger.LogInformation($"[vst] open call back current {Thread.CurrentThread.ManagedThreadId:X}");

                var result =
                    Dispatcher(
                        AEffect.Opcodes.effEditOpen,
                        default,
                        default,
                        hWindow.Handle,
                        default
                    );
                if (result == IntPtr.Zero)
                {
                    Logger.LogInformation("[vst] effEditOpen failed");
                }
            }

            {
                using var buffer = new PinnedBuffer<IntPtr>(new IntPtr());
                var result =
                    Dispatcher(
                        AEffect.Opcodes.effEditGetRect,
                        default,
                        default,
                        buffer.AddrOfPinnedObject,
                        default
                    );
                if (result == IntPtr.Zero)
                {
                    Logger.LogInformation("[vst] effEditGetRect failed");
                }

                EditorRect = Marshal.PtrToStructure<ERect>(buffer.Target);
                Logger.LogInformation($"[vst] effEditGetRect width;{EditorRect.right - EditorRect.left} height:{EditorRect.bottom - EditorRect.top}");

                width = EditorRect.right - EditorRect.left;
                height = EditorRect.bottom - EditorRect.top;

                //キャプチャ先を作成
                //bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            }
        }

        private void OnPreCloseWindow()
        {
            Logger.LogInformation($"[vst] close call back current {Thread.CurrentThread.ManagedThreadId:X}");

            {
                var result =
                    Dispatcher(
                        AEffect.Opcodes.effEditClose,
                        default,
                        default,
                        default,
                        default
                    );
                /*
                if (result == IntPtr.Zero)
                {
                    Logger.LogInformation("[vst] effEditClose failed");
                }
                */
            }

            /*
            bitmap?.Dispose();
            bitmap = default;
            */
        }
        private void OnPostPaint(HandleRef hWnd)
        {
            /*
            using var g = Graphics.FromImage(bitmap);
            var hdc = new HandleRef(this, g.GetHdc());
            try
            {
                NativeMethods.PrintWindow(hWnd, hdc, 0);
            }
            finally
            {
                g.ReleaseHdc(hdc.Handle);
            }
            bitmap.Save("C:\\a\\test" + Guid.NewGuid().ToString() + ".bmp");
            */
        }

        public async Task CloseEditor()
        {
            var aeffect = GetAEffect();
            if (!aeffect.flags.HasFlag(AEffect.VstAEffectFlags.effFlagsHasEditor))
            {
                Logger.LogInformation("[vst] effFlagsHasEditorではない");
                return;
            }

            if (window == default)
            {
                return;
            }

            window.Close();

            try
            {
                await windowTask.ConfigureAwait(false);
            }
            catch (WindowException e)
            {
                Logger.LogError(e, "[vst] Editor error");
            }
            Logger.LogInformation("[vst] Editor Closed");
        }

        private async Task Close()
        {
            await CloseEditor().ConfigureAwait(false);

            //stop
            Dispatcher(
                AEffect.Opcodes.effStopProcess,
                default,
                default,
                default,
                default
            );
            //suspend
            Dispatcher(
                AEffect.Opcodes.effMainsChanged,
                default,
                default,
                default,
                default
            );
            //close
            Dispatcher(
                AEffect.Opcodes.effClose,
                default,
                default,
                default,
                default
            );
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
                Logger.LogInformation("[vst] stop");

                Close().Wait();

                DispatcherProc = default;
                SetParameterProc = default;
                GetParameterProc = default;
                ProcessProc = default;

                audioMasterCallBack?.Dispose();
                audioMasterCallBack = default;

                events?.Dispose();
                events = default;

                eventList?.Dispose();
                eventList = default;
                /*
                bitmap?.Dispose();
                bitmap = default;
                */
            }

            disposed = true;
        }
    }
}