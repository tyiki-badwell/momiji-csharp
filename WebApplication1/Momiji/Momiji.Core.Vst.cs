﻿using Microsoft.Extensions.Logging;
using Momiji.Core.Wave;
using Momiji.Interop;
using Momiji.Interop.Kernel32;
using Momiji.Interop.Vst;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
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

        public VstBuffer(Int32 blockSize, int channels) : base(new IntPtr[channels])
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
                list.ForEach(buffer => { buffer.Dispose(); });
            }

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

        public Int32 SamplingRate { get; }
        public Int32 BlockSize { get; }

        public AudioMaster(
            Int32 samplingRate,
            Int32 blockSize,
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
            Int32 index,
            IntPtr value,
            IntPtr ptr,
            Single opt
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
        private DLL dll;
        internal IntPtr AEffectPtr { get; private set; }
        internal ERect EditorRect { get; private set; }

        private AEffect.DispatcherProc dispatcher;
        private AEffect.SetParameterProc setParameter;
        private AEffect.GetParameterProc getParameter;
        private AEffect.ProcessProc processReplacing;
        private AEffect.ProcessDoubleProc processDoubleReplacing;

        private AEffect aeffect;

        private int numOutputs;
        private AEffect.VstAEffectFlags flags;
        private AudioMaster<T> audioMaster;

        //private System.Windows.Input.ICommand window;

        private PinnedBuffer<byte[]> events;
        private PinnedBuffer<byte[]> eventList;
        private int sizeVstEvents = Marshal.SizeOf<VstEvents>();
        private int sizeVstMidiEvent = Marshal.SizeOf<VstMidiEvent>();
        private int sizeIntPtr = Marshal.SizeOf<IntPtr>();

        internal Effect(string library, AudioMaster<T> audioMaster, ILoggerFactory loggerFactory, Timer timer)
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Effect<T>>();
            Timer = timer;

            this.audioMaster = audioMaster;

            events = new PinnedBuffer<byte[]>(new byte[4000]); //TODO サイズが適当
            eventList = new PinnedBuffer<byte[]>(new byte[4000]); //TODO サイズが適当

            dll = DLL.LoadLibrary(library);
            if (dll.IsInvalid)
            {
                var error = Marshal.GetHRForLastWin32Error();
                Logger.LogInformation($"LoadLibrary error:{error}");
                Marshal.ThrowExceptionForHR(error);
            }

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

            AEffectPtr = vstPluginMain(audioMaster.AudioMasterCallBackProc);
            if (AEffectPtr == IntPtr.Zero)
            {
                throw new VstException("vstPluginMain で失敗");
            }
            aeffect = Marshal.PtrToStructure<AEffect>(AEffectPtr);
            numOutputs = aeffect.numOutputs;
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

            Logger.LogInformation("processReplacing:"+aeffect.processReplacing);
            Logger.LogInformation("processDoubleReplacing:"+aeffect.processDoubleReplacing);
            
            if (aeffect.dispatcher != IntPtr.Zero)
            {
                dispatcher =
                    Marshal.GetDelegateForFunctionPointer<AEffect.DispatcherProc>(aeffect.dispatcher);
            }

            if (aeffect.setParameter != IntPtr.Zero)
            {
                setParameter =
                    Marshal.GetDelegateForFunctionPointer<AEffect.SetParameterProc>(aeffect.setParameter);
            }

            if (aeffect.getParameter != IntPtr.Zero)
            {
                getParameter =
                    Marshal.GetDelegateForFunctionPointer<AEffect.GetParameterProc>(aeffect.getParameter);
            }

            if (aeffect.processReplacing != IntPtr.Zero)
            {
                processReplacing =
                    Marshal.GetDelegateForFunctionPointer<AEffect.ProcessProc>(aeffect.processReplacing);
            }
            else
            {
                throw new VstException("processReplacing が無い");
            }

            if (aeffect.processDoubleReplacing != IntPtr.Zero)
            {
                processDoubleReplacing =
                    Marshal.GetDelegateForFunctionPointer<AEffect.ProcessDoubleProc>(aeffect.processDoubleReplacing);
            }

        }

        private IntPtr Dispatcher(
            AEffect.Opcodes opcode,
            Int32 index,
            IntPtr value,
            IntPtr ptr,
            Single opt
        )
        {
            var result = dispatcher(
                    AEffectPtr,
                    opcode,
                    index,
                    value,
                    ptr,
                    opt
                );
            Logger.LogInformation($"[vst] dispatcher {opcode} {result}");
            return result;
        }

        public void Execute(
            VstBuffer<T> source,
            PcmBuffer<T> dest,
            IReceivableSourceBlock<VstMidiEvent> midiEventQueue
        )
        {
            var blockSize = audioMaster.BlockSize;

            {
                var list = new List<VstMidiEvent>();
                {
                    VstMidiEvent midiEvent;
                    while (midiEventQueue.TryReceive(out midiEvent))
                    {
                        list.Add(midiEvent);
                    }
                }

                if (list.Count > 0)
                {
                    var eventsPtr = events.AddrOfPinnedObject;
                    var eventListPtr = eventList.AddrOfPinnedObject;

                    var vstEvents = new VstEvents();
                    vstEvents.numEvents = list.Count;

                    //TODO 境界チェック
                    Marshal.StructureToPtr(vstEvents, eventsPtr, false);
                    eventsPtr += sizeVstEvents;

                    list.ForEach(midiEvent =>
                    {
                        //TODO 境界チェック
                        Marshal.StructureToPtr(midiEvent, eventListPtr, false);
                        Marshal.WriteIntPtr(eventsPtr, eventListPtr);
                        eventListPtr += sizeVstMidiEvent;
                        eventsPtr += sizeIntPtr;
                    });

                    source.Log.Add("[vst] start effProcessEvents", Timer.USecDouble);
                    Dispatcher(
                        AEffect.Opcodes.effProcessEvents,
                        0,
                        IntPtr.Zero,
                        events.AddrOfPinnedObject,
                        0
                    );
                    source.Log.Add("[vst] end effProcessEvents", Timer.USecDouble);
                }
            }

            source.Log.Add("[vst] start processReplacing", Timer.USecDouble);
            processReplacing(
                AEffectPtr,
                IntPtr.Zero,
                source.AddrOfPinnedObject,
                blockSize
            );
            source.Log.Add("[vst] end processReplacing", Timer.USecDouble);
            {
                dest.Log.Marge(source.Log);

                var target = dest.Target;
                var targetIdx = 0;
                var left = source[0];
                var right = source[1];

                dest.Log.Add("[to pcm] start", Timer.USecDouble);
                for (var idx = 0; idx < left.Length; idx++)
                {
                    target[targetIdx++] = left[idx];
                    target[targetIdx++] = right[idx];
                }
                dest.Log.Add("[to pcm] end", Timer.USecDouble);
            }
        }

        public async Task Run(
            ISourceBlock<VstBuffer<T>> sourceQueue,
            ITargetBlock<VstBuffer<T>> sourceReleaseQueue,
            ISourceBlock<PcmBuffer<T>> destQueue,
            ITargetBlock<PcmBuffer<T>> destReleaseQueue,
            IReceivableSourceBlock<VstMidiEvent> midiEventQueue,
            CancellationToken ct)
        {
            var blockSize = audioMaster.BlockSize;

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    var source = sourceQueue.Receive(ct);
                    var dest = destQueue.Receive(ct);

                    Execute(source, dest, midiEventQueue);

                    destReleaseQueue.Post(dest);
                    sourceReleaseQueue.Post(source);
                }
                Logger.LogInformation("[vst] loop end");
            });
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
                AEffect.Opcodes.effOpen,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                0
            );

            Dispatcher(
                AEffect.Opcodes.effSetSampleRate,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                audioMaster.SamplingRate
            );
            Dispatcher(
                AEffect.Opcodes.effSetBlockSize,
                0,
                new IntPtr(audioMaster.BlockSize),
                IntPtr.Zero,
                0
            );
            //resume
            Dispatcher(
                AEffect.Opcodes.effMainsChanged,
                0,
                new IntPtr(1),
                IntPtr.Zero,
                0
            );
            //start
            Dispatcher(
                AEffect.Opcodes.effStartProcess,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                0
            );

            /*
            Dispatcher(
                AEffect.Opcodes.effEditOpen,
                0,
                IntPtr.Zero,
                IntPtr.Zero, // TODO hWnd
                0
            );
            using (var buffer = new PinnedBuffer<IntPtr[]>(new IntPtr[1]))
            { 
                Dispatcher(
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
                AEffect.Opcodes.effEditClose,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                0
            );*/
            
            //stop
            Dispatcher(
                AEffect.Opcodes.effStopProcess,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                0
            );
            //suspend
            Dispatcher(
                AEffect.Opcodes.effMainsChanged,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                0
            );
            //close
            Dispatcher(
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

            if (disposing)
            {
                Logger.LogInformation("[vst] stop");
                Close();

                if (dll != null && !dll.IsInvalid)
                {
                    dll.Dispose();
                    dll = null;
                }

                events.Dispose();
                eventList.Dispose();
            }

            disposed = true;
        }
    }
}