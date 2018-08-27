using Microsoft.Extensions.Logging;
using Momiji.Interop;
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

        public T[] Get(int index)
        {
            return list[index].Target;
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

        public AudioMaster(Int32 samplingRate, Int32 blockSize, ILoggerFactory loggerFactory, Timer timer)
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
            effectMap.Add(effect.AeffectPtr, effect);
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
                        vstTimeInfo.Target.nanoSeconds = Timer.USec * 1000;

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
        private Kernel32.DynamicLinkLibrary dll;
        public IntPtr AeffectPtr { get; private set; }

        private AEffect.DispatcherProc dispatcher;
        private AEffect.SetParameterProc setParameter;
        private AEffect.GetParameterProc getParameter;
        private AEffect.ProcessProc processReplacing;
        private AEffect.ProcessDoubleProc processDoubleReplacing;

        int numOutputs;
        private AudioMaster<T> audioMaster;

        public Effect(string library, AudioMaster<T> audioMaster, ILoggerFactory loggerFactory, Timer timer)
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Effect<T>>();
            Timer = timer;

            this.audioMaster = audioMaster;

            dll = Kernel32.LoadLibrary(library);
            if (dll.IsInvalid)
            {
                var error = Marshal.GetHRForLastWin32Error();
                Logger.LogInformation($"LoadLibrary error:{error}");
                Marshal.ThrowExceptionForHR(error);
            }

            var proc = Kernel32.GetProcAddress(dll, "VSTPluginMain");
            if (proc == IntPtr.Zero)
            {
                proc = Kernel32.GetProcAddress(dll, "main");
            }

            if (proc == IntPtr.Zero)
            {
                var error = Marshal.GetHRForLastWin32Error();
                Logger.LogInformation($"GetProcAddress error:{error}");
                Marshal.ThrowExceptionForHR(error);
            }

            var vstPluginMain =
                Marshal.GetDelegateForFunctionPointer<AudioMaster.VSTPluginMain>(proc);

            AeffectPtr = vstPluginMain(audioMaster.AudioMasterCallBackProc);
            var aeffect = Marshal.PtrToStructure<AEffect>(AeffectPtr);
            numOutputs = aeffect.numOutputs;

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

            //Logger.LogInformation("processReplacing:"+aeffect.processReplacing);
            //Logger.LogInformation("processDoubleReplacing:"+aeffect.processDoubleReplacing);

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

            Open(audioMaster);
        }

        public async Task Run(
            ISourceBlock<Wave.PcmBuffer<T>> bufferQueue,
            ITargetBlock<Wave.PcmBuffer<T>> outputQueue,
            IReceivableSourceBlock<VstMidiEvent> midiEventQueue,
            CancellationToken ct)
        {
            var blockSize = audioMaster.BlockSize;
            using (var events = new PinnedBuffer<byte[]>(new byte[4000]))
            using (var eventList = new PinnedBuffer<byte[]>(new byte[4000]))
            using (var buffer = new VstBuffer<T>(blockSize, numOutputs))
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var sizeVstEvents = Marshal.SizeOf<VstEvents>();
                    var sizeVstMidiEvent = Marshal.SizeOf<VstMidiEvent>();
                    var sizeIntPtr = Marshal.SizeOf<IntPtr>();

                    var before = Timer.USecDouble;
                    var interval = (((double)audioMaster.BlockSize / audioMaster.SamplingRate) * 1000000.0);

                    using (var s = new SemaphoreSlim(1))
                    {
                        while (true)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                break;
                            }

                            var eventsPtr = events.AddrOfPinnedObject;
                            var eventListPtr = eventList.AddrOfPinnedObject;

                            //Logger.LogInformation("[vst] get data TRY");
                            var data = bufferQueue.Receive(ct);
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
                                //Logger.LogInformation($"[vst] get data OK [{diff}+{left}]us [{interval}]us");
                                before = after;
                            }

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

                                var processEventsResult =
                                    dispatcher(
                                        AeffectPtr,
                                        AEffect.Opcodes.effProcessEvents,
                                        0,
                                        IntPtr.Zero,
                                        events.AddrOfPinnedObject,
                                        0
                                    );
                                //    Logger.LogInformation($"effProcessEvents:{processEventsResult}");
                            }

                            processReplacing(
                                AeffectPtr,
                                IntPtr.Zero,
                                buffer.AddrOfPinnedObject,
                                blockSize
                            );

                            {
                                var target = data.Target;
                                var targetIdx = 0;
                                var left = buffer.Get(0);
                                var right = buffer.Get(1);

                                for (var idx = 0; idx < blockSize; idx++)
                                {
                                    target[targetIdx++] = left[idx];
                                    target[targetIdx++] = right[idx];
                                }
                            }

                            outputQueue.Post(data);
                        }
                    }
                    Logger.LogInformation("[vst] loop end");
                });
            }
        }

        private void Open(AudioMaster<T> audioMaster)
        {
            var openResult =
                dispatcher(
                    AeffectPtr,
                    AEffect.Opcodes.effOpen,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0
                );
            Logger.LogInformation($"effOpen:{openResult}");

            var setSampleRateResult =
                dispatcher(
                    AeffectPtr,
                    AEffect.Opcodes.effSetSampleRate,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    audioMaster.SamplingRate
                );
            Logger.LogInformation($"effSetSampleRate:{setSampleRateResult}");
            var setBlockSizeResult =
                dispatcher(
                    AeffectPtr,
                    AEffect.Opcodes.effSetBlockSize,
                    0,
                    new IntPtr(audioMaster.BlockSize),
                    IntPtr.Zero,
                    0
                );
            Logger.LogInformation($"effSetBlockSize:{setBlockSizeResult}");
            //resume
            var resumeResult =
                dispatcher(
                    AeffectPtr,
                    AEffect.Opcodes.effMainsChanged,
                    0,
                    new IntPtr(1),
                    IntPtr.Zero,
                    0
                );
            Logger.LogInformation($"effMainsChanged:{resumeResult}");
            //start
            var startProcessResult =
                dispatcher(
                    AeffectPtr,
                    AEffect.Opcodes.effStartProcess,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0
                );
            Logger.LogInformation($"effStartProcess:{startProcessResult}");
        }

        private void Close()
        {
            //stop
            var stopProcessResult =
                dispatcher(
                    AeffectPtr,
                    AEffect.Opcodes.effStopProcess,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0
                );
            Logger.LogInformation($"effStopProcess:{stopProcessResult}");
            //suspend
            var suspendResult =
                dispatcher(
                    AeffectPtr,
                    AEffect.Opcodes.effMainsChanged,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0
                );
            Logger.LogInformation($"effMainsChanged:{suspendResult}");
            //close
            var closeResult =
                dispatcher(
                    AeffectPtr,
                    AEffect.Opcodes.effClose,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0
                );
            Logger.LogInformation($"effClose:{closeResult}");

            AeffectPtr = IntPtr.Zero;
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
            }

            disposed = true;
        }
    }
}