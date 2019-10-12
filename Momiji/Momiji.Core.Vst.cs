using Microsoft.Extensions.Logging;
using Momiji.Core.Wave;
using Momiji.Core.WebMidi;
using Momiji.Interop;
using Momiji.Interop.Kernel32;
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
        public VstException(string message) : base(message)
        {
        }

    }

    public class VstBuffer<T> : PinnedBuffer<IntPtr[]> where T : struct
    {
        private bool disposed = false;
        private List<PinnedBuffer<T[]>> list = new List<PinnedBuffer<T[]>>();

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
            }

            list.ForEach(buffer => { buffer.Dispose(); });
            list.Clear();

            disposed = true;

            base.Dispose(disposing);
        }

        public T[] this[int index]
        {
            get
            {
                return list[index].Target;
            }
        }
    }

    public class AudioMaster<T> : IDisposable where T : struct
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private bool disposed = false;
        private IDictionary<IntPtr, Effect<T>> effectMap = new ConcurrentDictionary<IntPtr, Effect<T>>();

        private PinnedBuffer<VstTimeInfo> vstTimeInfo;

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
            effectMap.Add(effect.AEffectPtr, effect);
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
            }

            Logger.LogInformation("[vst host] stop");
            foreach (var (ptr, effect) in effectMap)
            {
                Logger.LogInformation("[vst] try stop");
                effect.Dispose();
            }
            effectMap.Clear();
            vstTimeInfo.Dispose();

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
                    Logger.LogInformation($"AudioMasterCallBackProc NOP opcode:{opcode:F}");
                    return IntPtr.Zero;
            }
        }
    }


    public class Effect<T> : IDisposable where T : struct
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private bool disposed = false;
        //private DLL dll;
        private Dll dll;
        internal IntPtr AEffectPtr { get; private set; }
        //internal ERect EditorRect { get; private set; }

        private AEffect.DispatcherProc Dispatcher { get; }
        private AEffect.SetParameterProc SetParameter { get; }
        private AEffect.GetParameterProc GetParameter { get; }
        private AEffect.ProcessProc ProcessReplacing { get; }
        private AEffect.ProcessDoubleProc ProcessDoubleReplacing { get; }

        private int NumOutputs { get; }
        private AEffect.VstAEffectFlags flags;

        private AudioMaster<T> audioMaster;
        private PinnedDelegate<AudioMaster.CallBack> audioMasterCallBack;

        //private System.Windows.Input.ICommand window;

        private PinnedBuffer<byte[]> events;
        private PinnedBuffer<byte[]> eventList;

        private double beforeTime;
        private MIDIMessageEvent2? extraMidiEvent;

        private int SIZE_OF_VSTEVENTS { get; }
        private int SIZE_OF_VSTMIDIEVENT { get; }
        private int SIZE_OF_INTPTR { get; }
        private int COUNT_OF_EVENTS { get; }

        internal Effect(string library, AudioMaster<T> audioMaster, ILoggerFactory loggerFactory, Timer timer)
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Effect<T>>();
            Timer = timer;

            SIZE_OF_VSTEVENTS = Marshal.SizeOf<VstEvents>();
            SIZE_OF_VSTMIDIEVENT = Marshal.SizeOf<VstMidiEvent>();
            SIZE_OF_INTPTR = Marshal.SizeOf<IntPtr>();
            COUNT_OF_EVENTS = 500; //サイズが適当

            this.audioMaster = audioMaster;

            events = new PinnedBuffer<byte[]>(new byte[SIZE_OF_VSTEVENTS + (SIZE_OF_INTPTR * COUNT_OF_EVENTS)]);
            eventList = new PinnedBuffer<byte[]>(new byte[SIZE_OF_VSTMIDIEVENT * COUNT_OF_EVENTS]);

            dll = new Dll(library);
            /*
            dll = DLL.LoadLibrary(library);
            if (dll.IsInvalid)
            {
                var error = Marshal.GetHRForLastWin32Error();
                Logger.LogInformation($"LoadLibrary error:{error}");
                Marshal.ThrowExceptionForHR(error);
            }
            */

            var vstPluginMain = dll.GetExport<AEffect.VSTPluginMain>("VSTPluginMain");
            if (vstPluginMain == default)
            {
                vstPluginMain = dll.GetExport<AEffect.VSTPluginMain>("main");
            }

            /*
            var proc = dll.GetProcAddress("VSTPluginMain");
            if (proc == IntPtr.Zero)
            {
                proc = dll.GetProcAddress("main");
            }

            if (proc == IntPtr.Zero)
            {
                var error = Marshal.GetHRForLastWin32Error();
                Logger.LogInformation($"GetProcAddress error:{error}");
                Marshal.ThrowExceptionForHR(error);
            }

            var vstPluginMain =
                Marshal.GetDelegateForFunctionPointer<AEffect.VSTPluginMain>(proc);
                */

            audioMasterCallBack = new PinnedDelegate<AudioMaster.CallBack>(new AudioMaster.CallBack(audioMaster.AudioMasterCallBackProc));
            AEffectPtr = vstPluginMain(audioMasterCallBack.FunctionPointer);
            if (AEffectPtr == IntPtr.Zero)
            {
                throw new VstException("vstPluginMain で失敗");
            }

            unsafe
            {
                var aeffect = new Span<AEffect>((void*)AEffectPtr, 1);
                NumOutputs = aeffect[0].numOutputs;
                flags = aeffect[0].flags;

                Logger.LogInformation($"magic:{aeffect[0].magic}");
                Logger.LogInformation($"dispatcher:{aeffect[0].dispatcher}");
                Logger.LogInformation($"processDeprecated:{aeffect[0].processDeprecated}");
                Logger.LogInformation($"setParameter:{aeffect[0].setParameter}");
                Logger.LogInformation($"getParameter:{aeffect[0].getParameter}");

                Logger.LogInformation($"numPrograms:{aeffect[0].numPrograms}");
                Logger.LogInformation($"numParams:{aeffect[0].numParams}");
                Logger.LogInformation($"numInputs:{aeffect[0].numInputs}");
                Logger.LogInformation($"numOutputs:{aeffect[0].numOutputs}");
                Logger.LogInformation($"flags:{aeffect[0].flags}");

                //Logger.LogInformation($"resvd1:"+aeffect.resvd1);
                //Logger.LogInformation($"resvd2:"+aeffect.resvd2);

                Logger.LogInformation($"initialDelay:{aeffect[0].initialDelay}");

                Logger.LogInformation($"realQualitiesDeprecated:{aeffect[0].realQualitiesDeprecated}");
                Logger.LogInformation($"offQualitiesDeprecated:{aeffect[0].offQualitiesDeprecated}");
                Logger.LogInformation($"ioRatioDeprecated:{aeffect[0].ioRatioDeprecated}");
                //Logger.LogInformation($"object:"+aeffect._object);
                Logger.LogInformation($"user:{aeffect[0].user}");

                Logger.LogInformation($"uniqueID:{aeffect[0].uniqueID}");
                Logger.LogInformation($"version:{aeffect[0].version}");

                Logger.LogInformation("processReplacing:" + aeffect[0].processReplacing);
                Logger.LogInformation("processDoubleReplacing:" + aeffect[0].processDoubleReplacing);

                if (aeffect[0].dispatcher != IntPtr.Zero)
                {
                    Dispatcher =
                        Marshal.GetDelegateForFunctionPointer<AEffect.DispatcherProc>(aeffect[0].dispatcher);
                }

                if (aeffect[0].setParameter != IntPtr.Zero)
                {
                    SetParameter =
                        Marshal.GetDelegateForFunctionPointer<AEffect.SetParameterProc>(aeffect[0].setParameter);
                }

                if (aeffect[0].getParameter != IntPtr.Zero)
                {
                    GetParameter =
                        Marshal.GetDelegateForFunctionPointer<AEffect.GetParameterProc>(aeffect[0].getParameter);
                }

                if (aeffect[0].processReplacing != IntPtr.Zero)
                {
                    ProcessReplacing =
                        Marshal.GetDelegateForFunctionPointer<AEffect.ProcessProc>(aeffect[0].processReplacing);
                }
                else
                {
                    throw new VstException("processReplacing が無い");
                }

                if (aeffect[0].processDoubleReplacing != IntPtr.Zero)
                {
                    ProcessDoubleReplacing =
                        Marshal.GetDelegateForFunctionPointer<AEffect.ProcessDoubleProc>(aeffect[0].processDoubleReplacing);
                }
            }

            beforeTime = Timer.USecDouble;
        }

        ~Effect()
        {
            Dispose(false);
        }

        public void Execute(
            VstBuffer<T> source,
            Task<PcmBuffer<T>> destTask,
            IReceivableSourceBlock<MIDIMessageEvent2> midiEventInput,
            ITargetBlock<MIDIMessageEvent2> midiEventOutput = null
        )
        {
            var nowTime = Timer.USecDouble;
            //Logger.LogInformation($"[vst] start {DateTimeOffset.FromUnixTimeMilliseconds((long)(beforeTime / 1000)).ToUniversalTime():HH:mm:ss.fff} {DateTimeOffset.FromUnixTimeMilliseconds((long)(nowTime / 1000)).ToUniversalTime():HH:mm:ss.fff} {nowTime - beforeTime}");

            var samplingRate = audioMaster.SamplingRate;
            var blockSize = audioMaster.BlockSize;
            
            {
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
                        /*
                        Logger.LogInformation(
                            $"note " +
                            $"{DateTimeOffset.FromUnixTimeMilliseconds((long)item.midiMessageEvent.receivedTime).ToUniversalTime():HH:mm:ss.fff} " +
                            $"{DateTimeOffset.FromUnixTimeMilliseconds((long)item.receivedTimeUSec / 1000).ToUniversalTime():HH:mm:ss.fff} " +
                            $"{DateTimeOffset.FromUnixTimeMilliseconds((long)(Timer.USecDouble/1000)).ToUniversalTime():HH:mm:ss.fff} " +
                            $"=> " +
                            $"{item.midiMessageEvent.data0:X2}" +
                            $"{item.midiMessageEvent.data1:X2}" +
                            $"{item.midiMessageEvent.data2:X2}" +
                            $"{item.midiMessageEvent.data3:X2}"
                        );
                        */
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
                        /*
                        Logger.LogInformation(
                            $"[vst] deltaFrames " +
                            $"{vstEvent.deltaFrames} " +
                            $"{blockSize} " +
                            $"{samplingRate} " +
                            $"{deltaTime} "
                            );
                            */
                        //TODO 境界チェック
                        Marshal.StructureToPtr(vstEvent, eventListPtr, false);
                        Marshal.WriteIntPtr(eventsPtr, eventListPtr);
                        eventListPtr += SIZE_OF_VSTMIDIEVENT;
                        eventsPtr += SIZE_OF_INTPTR;
                    });

                    source.Log.Add("[vst] start effProcessEvents", Timer.USecDouble);
                    Dispatcher(
                        AEffectPtr,
                        AEffect.Opcodes.effProcessEvents,
                        0,
                        IntPtr.Zero,
                        events.AddrOfPinnedObject,
                        0
                    );
                    source.Log.Add("[vst] end effProcessEvents", Timer.USecDouble);
                    //Logger.LogInformation($"[vst] end effProcessEvents {DateTimeOffset.FromUnixTimeMilliseconds((long)(Timer.USecDouble/1000)).ToUniversalTime():HH:mm:ss.fff}");
                }
            }

            source.Log.Add("[vst] start processReplacing", Timer.USecDouble);
            ProcessReplacing(
                AEffectPtr,
                IntPtr.Zero,
                source.AddrOfPinnedObject,
                blockSize
            );
            source.Log.Add("[vst] end processReplacing", Timer.USecDouble);
            var dest = destTask.Result;
            unsafe
            {
                dest.Log.Marge(source.Log);

                var targetIdx = 0;
                var target = new Span<T>(dest.Target);
                var left = new ReadOnlySpan<T>(source[0]);
                var right = new ReadOnlySpan<T>(source[1]);

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

        internal void Open()
        {
            if (!flags.HasFlag(AEffect.VstAEffectFlags.effFlagsIsSynth))
            {
                throw new VstException("effFlagsIsSynth ではない");
            }
            /*if (!flags.HasFlag(AEffect.VstAEffectFlags.effFlagsHasEditor))
            {
                throw new VstException("effFlagsHasEditor ではない");
            }*/

            Dispatcher(
                AEffectPtr,
                AEffect.Opcodes.effOpen,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                0
            );

            Dispatcher(
                AEffectPtr,
                AEffect.Opcodes.effSetSampleRate,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                audioMaster.SamplingRate
            );
            Dispatcher(
                AEffectPtr,
                AEffect.Opcodes.effSetBlockSize,
                0,
                new IntPtr(audioMaster.BlockSize),
                IntPtr.Zero,
                0
            );
            //resume
            Dispatcher(
                AEffectPtr,
                AEffect.Opcodes.effMainsChanged,
                0,
                new IntPtr(1),
                IntPtr.Zero,
                0
            );
            //start
            Dispatcher(
                AEffectPtr,
                AEffect.Opcodes.effStartProcess,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                0
            );

            /*
            Dispatcher(
                AEffectPtr,
                AEffect.Opcodes.effEditOpen,
                0,
                IntPtr.Zero,
                IntPtr.Zero, // TODO hWnd
                0
            );
            using (var buffer = new PinnedBuffer<IntPtr[]>(new IntPtr[1]))
            { 
                Dispatcher(
                    AEffectPtr,
                    AEffect.Opcodes.effEditGetRect,
                    0,
                    IntPtr.Zero,
                    buffer.AddrOfPinnedObject, // TODO out ERect
                    0
                );

                EditorRect = Marshal.PtrToStructure<ERect>(buffer.AddrOfPinnedObject);
            }*/
        }

        private void Close()
        {
            /*
            Dispatcher(
                AEffectPtr,
                AEffect.Opcodes.effEditClose,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                0
            );*/

            //stop
            Dispatcher(
                AEffectPtr,
                AEffect.Opcodes.effStopProcess,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                0
            );
            //suspend
            Dispatcher(
                AEffectPtr,
                AEffect.Opcodes.effMainsChanged,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                0
            );
            //close
            Dispatcher(
                AEffectPtr,
                AEffect.Opcodes.effClose,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                0
            );

            AEffectPtr = IntPtr.Zero;
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
            }

            audioMasterCallBack?.Dispose();
            audioMasterCallBack = null;

            if (dll != null && !dll.IsInvalid)
            {
                dll.Dispose();
                dll = null;
            }

            events?.Dispose();
            events = null;

            eventList?.Dispose();
            eventList = null;

            disposed = true;
        }
    }
}