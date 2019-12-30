using Microsoft.Extensions.Logging;
using Momiji.Core.Wave;
using Momiji.Core.WebMidi;
using Momiji.Interop;
using Momiji.Interop.Vst;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    public class VstBuffer<T> : PinnedBufferWithLog<IntPtr[]> where T : struct
    {
        private bool disposed = false;
        private readonly List<PinnedBuffer<T[]>> list = new List<PinnedBuffer<T[]>>();

        public VstBuffer(int blockSize, int channels) : base(new IntPtr[channels])
        {
            for (var i = 0; i < channels; i++)
            {
                var buffer = new PinnedBuffer<T[]>(new T[blockSize]);
                list.Add(buffer);
                Target[i] = buffer.AddrOfPinnedObject;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposed) return;

            if (disposing)
            {
                list.ForEach(buffer => buffer.Dispose());
                list.Clear();
            }

            disposed = true;

            base.Dispose(disposing);
        }

        public T[] GetChannelBuffer(int channel) => list[channel].Target;
    }

    public class AudioMaster<T> : IDisposable where T : struct
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private bool disposed = false;
        private readonly IDictionary<IntPtr, Effect<T>> effectMap = new ConcurrentDictionary<IntPtr, Effect<T>>();

        private readonly PinnedBuffer<VstTimeInfo> vstTimeInfo;

        public int SamplingRate { get; }
        public int BlockSize { get; }

        public AudioMaster(
            int samplingRate,
            int blockSize,
            ILoggerFactory loggerFactory,
            Timer timer
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<AudioMaster<T>>();
            Timer = timer;

            SamplingRate = samplingRate;
            BlockSize = blockSize;

            var timeInfo = new VstTimeInfo();
            vstTimeInfo = new PinnedBuffer<VstTimeInfo>(timeInfo);

            timeInfo.samplePos = 0.0;
            timeInfo.sampleRate = samplingRate;
            timeInfo.nanoSeconds = 0.0;
            timeInfo.ppqPos = 0.0;
            timeInfo.tempo = 240.0;
            timeInfo.barStartPos = 0.0;
            timeInfo.cycleStartPos = 0.0;
            timeInfo.cycleEndPos = 0.0;
            timeInfo.timeSigNumerator = 4;
            timeInfo.timeSigDenominator = 4;
            timeInfo.smpteOffset = 0;
            timeInfo.smpteFrameRate = VstTimeInfo.VstSmpteFrameRate.kVstSmpte24fps;
            timeInfo.samplesToNextClock = 0;
            timeInfo.flags = VstTimeInfo.VstTimeInfoFlags.kVstTempoValid | VstTimeInfo.VstTimeInfoFlags.kVstNanosValid;
        }

        ~AudioMaster()
        {
            Dispose(false);
        }

        public Effect<T> AddEffect(string library)
        {
            var effect = new Effect<T>(library, this, LoggerFactory, Timer);
            effectMap.Add(effect.aeffectPtr, effect);
            effect.Open();
            return effect;
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
                Logger.LogInformation("[vst host] stop");
                foreach (var (ptr, effect) in effectMap)
                {
                    Logger.LogInformation("[vst] try stop");
                    effect.Dispose();
                }
                effectMap.Clear();
                vstTimeInfo.Dispose();
            }

            disposed = true;
        }

        internal IntPtr AudioMasterCallBackProc(
            IntPtr/*AEffect^*/		aeffectPtr,
            AudioMaster.Opcodes opcode,
            int index,
            IntPtr value,
            IntPtr ptr,
            float opt
        )
        {
            switch (opcode)
            {
                case AudioMaster.Opcodes.audioMasterVersion:
                    {
                        return new IntPtr(2400);
                    }
                case AudioMaster.Opcodes.audioMasterGetTime:
                    {
                        vstTimeInfo.Target.nanoSeconds = Timer.USecDouble * 1000;

                        return vstTimeInfo.AddrOfPinnedObject;
                    }
                case AudioMaster.Opcodes.audioMasterGetSampleRate:
                    return new IntPtr(SamplingRate);

                case AudioMaster.Opcodes.audioMasterGetBlockSize:
                    return new IntPtr(BlockSize);

                default:
                    Logger.LogInformation(
                        $"AudioMasterCallBackProc NOP " +
                        $"{nameof(aeffectPtr)}:{aeffectPtr:X} " +
                        $"{nameof(opcode)}:{opcode:F} " +
                        $"{nameof(index)}:{index} " +
                        $"{nameof(value)}:{value:X} " +
                        $"{nameof(ptr)}:{ptr:X} " +
                        $"{nameof(opt)}:{opt}"
                    );
                    return default;
            }
        }
    }


    public class Effect<T> : IDisposable where T : struct
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private bool disposed = false;

        private Dll dll;
        internal readonly IntPtr aeffectPtr;
        //internal ERect EditorRect { get; private set; }

        private AEffect.DispatcherProc DispatcherProc { get; }
        private AEffect.SetParameterProc SetParameterProc { get; }
        private AEffect.GetParameterProc GetParameterProc { get; }
        private AEffect.ProcessProc ProcessProc { get; }
        private AEffect.ProcessDoubleProc ProcessDoubleProc { get; }

        private int NumOutputs { get; }
        private readonly AEffect.VstAEffectFlags flags;

        private readonly AudioMaster<T> audioMaster;
        private PinnedDelegate<AudioMaster.CallBack> audioMasterCallBack;

        //private System.Windows.Input.ICommand window;

        private PinnedBuffer<byte[]> events;
        private PinnedBuffer<byte[]> eventList;

        private double beforeTime;
        private MIDIMessageEvent2? extraMidiEvent;

        private static readonly int SIZE_OF_VSTEVENTS = Marshal.SizeOf<VstEvents>();
        private static readonly int SIZE_OF_VSTMIDIEVENT = Marshal.SizeOf<VstMidiEvent>();
        private static readonly int SIZE_OF_INTPTR = Marshal.SizeOf<IntPtr>();
        private static readonly int COUNT_OF_EVENTS = 500; //サイズが適当

        internal Effect(string library, AudioMaster<T> audioMaster, ILoggerFactory loggerFactory, Timer timer)
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Effect<T>>();
            Timer = timer;

            this.audioMaster = audioMaster;

            //TODO その場で作っても問題ないか？
            events = new PinnedBuffer<byte[]>(new byte[SIZE_OF_VSTEVENTS + (SIZE_OF_INTPTR * COUNT_OF_EVENTS)]);
            eventList = new PinnedBuffer<byte[]>(new byte[SIZE_OF_VSTMIDIEVENT * COUNT_OF_EVENTS]);

            dll = new Dll(library);
            if (dll.IsInvalid)
            {
                throw new VstException($"DLLの読み込みに失敗{library}");
            }

            var vstPluginMain = dll.GetExport<AEffect.VSTPluginMain>("VSTPluginMain");
            if (vstPluginMain == default)
            {
                vstPluginMain = dll.GetExport<AEffect.VSTPluginMain>("main");
            }

            if (vstPluginMain == default)
            {
                throw new VstException("VSTPluginMainが取得できない");
            }

            audioMasterCallBack = new PinnedDelegate<AudioMaster.CallBack>(new AudioMaster.CallBack(audioMaster.AudioMasterCallBackProc));
            aeffectPtr = vstPluginMain(audioMasterCallBack.FunctionPointer);
            if (aeffectPtr == default)
            {
                throw new VstException("vstPluginMain で失敗");
            }

            var aeffect = GetAEffect();

            NumOutputs = aeffect.numOutputs;
            flags = aeffect.flags;

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

            if (aeffect.processReplacing != default)
            {
                ProcessProc =
                    Marshal.GetDelegateForFunctionPointer<AEffect.ProcessProc>(aeffect.processReplacing);
            }
            else
            {
                throw new VstException("processReplacing が無い");
            }

            if (aeffect.processDoubleReplacing != default)
            {
                ProcessDoubleProc =
                    Marshal.GetDelegateForFunctionPointer<AEffect.ProcessDoubleProc>(aeffect.processDoubleReplacing);
            }

            beforeTime = Timer.USecDouble;
        }

        ~Effect()
        {
            Dispose(false);
        }

        public ref AEffect GetAEffect()
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

        private string GetString(AEffect.Opcodes opcode, int index, int length)
        {
            if (DispatcherProc == default)
            {
                return default;
            }

            using var buffer = new PinnedBuffer<byte[]>(new byte[length+1]);

            DispatcherProc(
                aeffectPtr,
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
            VstBuffer<T> source,
            Task<PcmBuffer<T>> destTask,
            IReceivableSourceBlock<MIDIMessageEvent2> midiEventInput,
            ITargetBlock<MIDIMessageEvent2> midiEventOutput = null
        )
        {
            if (DispatcherProc == default)
            {
                return;
            }
            if (source == default)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var blockSize = audioMaster.BlockSize;
            var nowTime = Timer.USecDouble;
            Logger.LogInformation($"[vst] start {DateTimeOffset.FromUnixTimeMilliseconds((long)(beforeTime / 1000)).ToUniversalTime():HH:mm:ss.fff} {DateTimeOffset.FromUnixTimeMilliseconds((long)(nowTime / 1000)).ToUniversalTime():HH:mm:ss.fff} {nowTime - beforeTime}");

            // processReplacingの前に１度しか呼べない制限がある・・・？
            ProcessEvent(source, nowTime, midiEventInput, midiEventOutput);

            source.Log.Add("[vst] start processReplacing", Timer.USecDouble);
            ProcessProc(
                aeffectPtr,
                default,
                source.AddrOfPinnedObject,
                blockSize
            );
            source.Log.Add("[vst] end processReplacing", Timer.USecDouble);
            var dest = destTask.Result;
            
            {
                //TODO ココも遅い？
                dest.Log.Marge(source.Log);

                var targetIdx = 0;
                var target = new Span<T>(dest.Target);
                var left = new ReadOnlySpan<T>(source.GetChannelBuffer(0));
                var right = new ReadOnlySpan<T>(source.GetChannelBuffer(1));

                dest.Log.Add("[to pcm] start", Timer.USecDouble);
                for (var idx = 0; idx < left.Length; idx++)
                {
                    target[targetIdx++] = left[idx];
                    target[targetIdx++] = right[idx];
                }
                dest.Log.Add("[to pcm] end", Timer.USecDouble);
            }

            beforeTime = nowTime;
        }

        public VstBuffer<T> ProcessReplacing(
            VstBuffer<T> source
        )
        {
            if (DispatcherProc == default)
            {
                return default;
            }
            if (source == default)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var blockSize = audioMaster.BlockSize;

            source.Log.Add("[vst] start processReplacing", Timer.USecDouble);
            ProcessProc(
                aeffectPtr,
                default,
                source.AddrOfPinnedObject,
                blockSize
            );
            source.Log.Add("[vst] end processReplacing", Timer.USecDouble);

            return source;
        }

        public void ProcessEvent(
            VstBuffer<T> source,
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
                midiEventOutput?.SendAsync(extraMidiEvent.Value);
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
                    source.Log.Add(
                        $"[vst] midiEvent " +
                        $"{item.midiMessageEvent.data0:X2}" +
                        $"{item.midiMessageEvent.data1:X2}" +
                        $"{item.midiMessageEvent.data2:X2}" +
                        $"{item.midiMessageEvent.data3:X2}",
                        item.receivedTimeUSec
                    );
                    list.Add(item);
                    midiEventOutput?.SendAsync(item);
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

                source.Log.Add("[vst] start effProcessEvents", Timer.USecDouble);
                DispatcherProc(
                    aeffectPtr,
                    AEffect.Opcodes.effProcessEvents,
                    default,
                    default,
                    events.AddrOfPinnedObject,
                    default
                );
                source.Log.Add("[vst] end effProcessEvents", Timer.USecDouble);
            }
        }
        internal void Open()
        {
            if (DispatcherProc == default)
            {
                return;
            }

            if (!flags.HasFlag(AEffect.VstAEffectFlags.effFlagsIsSynth))
            {
                throw new VstException("effFlagsIsSynth ではない");
            }
            /*if (!flags.HasFlag(AEffect.VstAEffectFlags.effFlagsHasEditor))
            {
                throw new VstException("effFlagsHasEditor ではない");
            }*/

            // open
            DispatcherProc(
                aeffectPtr,
                AEffect.Opcodes.effOpen,
                default,
                default,
                default,
                default
            );

            // set sampling rate
            DispatcherProc(
                aeffectPtr,
                AEffect.Opcodes.effSetSampleRate,
                default,
                default,
                default,
                audioMaster.SamplingRate
            );

            // set block size
            DispatcherProc(
                aeffectPtr,
                AEffect.Opcodes.effSetBlockSize,
                default,
                new IntPtr(audioMaster.BlockSize),
                default,
                default
            );

            // resume
            DispatcherProc(
                aeffectPtr,
                AEffect.Opcodes.effMainsChanged,
                default,
                new IntPtr(1),
                default,
                default
            );

            // start
            DispatcherProc(
                aeffectPtr,
                AEffect.Opcodes.effStartProcess,
                default,
                default,
                default,
                default
            );

            /*
            Dispatcher(
                aeffectPtr,
                AEffect.Opcodes.effEditOpen,
                default,
                default,
                default, // TODO hWnd
                default
            );
            using (var buffer = new PinnedBuffer<IntPtr[]>(new IntPtr[1]))
            { 
                Dispatcher(
                    aeffectPtr,
                    AEffect.Opcodes.effEditGetRect,
                    default,
                    default,
                    buffer.AddrOfPinnedObject, // TODO out ERect
                    default
                );

                EditorRect = Marshal.PtrToStructure<ERect>(buffer.AddrOfPinnedObject);
            }*/
        }

        private void Close()
        {
            if (DispatcherProc == default)
            {
                return;
            }
            /*
            Dispatcher(
                aeffectPtr,
                AEffect.Opcodes.effEditClose,
                default,
                default,
                default,
                default
            );*/

            //stop
            DispatcherProc(
                aeffectPtr,
                AEffect.Opcodes.effStopProcess,
                default,
                default,
                default,
                default
            );
            //suspend
            DispatcherProc(
                aeffectPtr,
                AEffect.Opcodes.effMainsChanged,
                default,
                default,
                default,
                default
            );
            //close
            DispatcherProc(
                aeffectPtr,
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

            Logger.LogInformation("[vst] stop");
            Close();

            if (disposing)
            {
                audioMasterCallBack?.Dispose();
                audioMasterCallBack = null;

                events?.Dispose();
                events = null;

                eventList?.Dispose();
                eventList = null;
            }

            dll?.Dispose();
            dll = null;

            disposed = true;
        }
    }
}