using Microsoft.Extensions.Logging;
using Momiji.Interop;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using static Momiji.Interop.Vst;
using static Momiji.Interop.Vst.VstTimeInfo;

namespace Momiji.Core.Vst
{
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
                Target[i] = buffer.AddrOfPinnedObject();
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

        private bool disposed = false;
        private IDictionary<IntPtr, Effect<T>> effectMap = new ConcurrentDictionary<IntPtr, Effect<T>>();

        private PinnedBuffer<VstTimeInfo> vstTimeInfo;

        public Int32 SamplingRate { get; }
        public Int32 BlockSize { get; }

        public AudioMaster(Int32 samplingRate, Int32 blockSize, ILoggerFactory loggerFactory)
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<AudioMaster<T>>();

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
            timeInfo.smpteFrameRate = VstSmpteFrameRate.kVstSmpte24fps;
            timeInfo.samplesToNextClock = 0;
            timeInfo.flags = VstTimeInfoFlags.kVstTempoValid | VstTimeInfoFlags.kVstNanosValid;
        }

        public Effect<T> AddEffect(string library)
        {
            var effect = new Effect<T>(library, this, LoggerFactory);
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
            AudioMasterOpcodes opcode,
            Int32 index,
            IntPtr value,
            IntPtr ptr,
            Single opt
        )
        {
            switch (opcode)
            {
                case AudioMasterOpcodes.audioMasterVersion:
                    {
                        return new IntPtr(2400);
                    }
                case AudioMasterOpcodes.audioMasterGetTime:
                    {
                        vstTimeInfo.Target.nanoSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000;

                        return vstTimeInfo.AddrOfPinnedObject();
                    }
                case AudioMasterOpcodes.audioMasterGetSampleRate:
                    return new IntPtr(SamplingRate);

                case AudioMasterOpcodes.audioMasterGetBlockSize:
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

        private bool disposed = false;
        private Kernel32.DynamicLinkLibrary dll;
        public IntPtr AeffectPtr { get; private set; }

        private AEffectDispatcherProc dispatcher;
        private AEffectSetParameterProc setParameter;
        private AEffectGetParameterProc getParameter;
        private AEffectProcessProc processReplacing;
        private AEffectProcessDoubleProc processDoubleReplacing;

        int numOutputs;
        private AudioMaster<T> audioMaster;

        public Effect(string library, AudioMaster<T> audioMaster, ILoggerFactory loggerFactory)
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Effect<T>>();

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
                Marshal.GetDelegateForFunctionPointer<VSTPluginMain>(proc);

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
                    Marshal.GetDelegateForFunctionPointer<AEffectDispatcherProc>(aeffect.dispatcher);
            }

            if (aeffect.setParameter != IntPtr.Zero)
            {
                setParameter =
                    Marshal.GetDelegateForFunctionPointer<AEffectSetParameterProc>(aeffect.setParameter);
            }

            if (aeffect.getParameter != IntPtr.Zero)
            {
                getParameter =
                    Marshal.GetDelegateForFunctionPointer<AEffectGetParameterProc>(aeffect.getParameter);
            }

            if (aeffect.processReplacing != IntPtr.Zero)
            {
                processReplacing =
                    Marshal.GetDelegateForFunctionPointer<AEffectProcessProc>(aeffect.processReplacing);
            }

            if (aeffect.processDoubleReplacing != IntPtr.Zero)
            {
                processDoubleReplacing =
                    Marshal.GetDelegateForFunctionPointer<AEffectProcessDoubleProc>(aeffect.processDoubleReplacing);
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

                    var stopwatch = Stopwatch.StartNew();
                    var before = stopwatch.ElapsedMilliseconds;
                    var interval = (long)(audioMaster.BlockSize / (float)audioMaster.SamplingRate * 1000.0);

                    using (var s = new SemaphoreSlim(1))
                    {
                        while (true)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                break;
                            }

                            var eventsPtr = events.AddrOfPinnedObject();
                            var eventListPtr = eventList.AddrOfPinnedObject();

                            try
                            {
                                //Logger.LogInformation("[vst] get data TRY");
                                var data = bufferQueue.Receive(new TimeSpan(20_000_000), ct);
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
                                //    Logger.LogInformation($"[vst] get data OK [{diff}+{left}]ms [{interval}]ms ");
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

                                    Marshal.StructureToPtr(vstEvents, eventsPtr, false);
                                    eventsPtr += sizeVstEvents;

                                    list.ForEach(midiEvent =>
                                    {
                                        Marshal.StructureToPtr(midiEvent, eventListPtr, false);
                                        Marshal.WriteIntPtr(eventsPtr, eventListPtr);
                                        eventListPtr += sizeVstMidiEvent;
                                        eventsPtr += sizeIntPtr;
                                    });

                                    var processEventsResult =
                                        dispatcher(
                                            AeffectPtr,
                                            AEffectOpcodes.effProcessEvents,
                                            0,
                                            IntPtr.Zero,
                                            events.AddrOfPinnedObject(),
                                            0
                                        );
                                //    Logger.LogInformation($"effProcessEvents:{processEventsResult}");
                                }

                                processReplacing(
                                    AeffectPtr,
                                    IntPtr.Zero,
                                    buffer.AddrOfPinnedObject(),
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

                                var finish = stopwatch.ElapsedMilliseconds;

                            //    Logger.LogInformation($"[vst] post data:[{finish - before}]ms");
                            }
                            catch (TimeoutException te)
                            {
                                Logger.LogInformation("[vst] timeout");
                                continue;
                            }
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
                    AEffectOpcodes.effOpen,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0
                );
            Logger.LogInformation($"effOpen:{openResult}");

            var setSampleRateResult =
                dispatcher(
                    AeffectPtr,
                    AEffectOpcodes.effSetSampleRate,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    audioMaster.SamplingRate
                );
            Logger.LogInformation($"effSetSampleRate:{setSampleRateResult}");
            var setBlockSizeResult =
                dispatcher(
                    AeffectPtr,
                    AEffectOpcodes.effSetBlockSize,
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
                    AEffectOpcodes.effMainsChanged,
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
                    AEffectOpcodes.effStartProcess,
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
                    AEffectOpcodes.effStopProcess,
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
                    AEffectOpcodes.effMainsChanged,
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
                    AEffectOpcodes.effClose,
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