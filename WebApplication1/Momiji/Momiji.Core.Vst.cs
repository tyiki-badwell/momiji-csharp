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
    public class VstBuffer<T> : PinnedBuffer<IntPtr[]>
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

    public class AudioMaster<T> : IDisposable
    {
        private bool disposed = false;
        private IDictionary<IntPtr, Effect<T>> effectMap = new ConcurrentDictionary<IntPtr, Effect<T>>();

        private PinnedBuffer<VstTimeInfo> vstTimeInfo;

        public Int32 SamplingRate { get; }
        public Int32 BlockSize { get; }

        public AudioMaster(Int32 samplingRate, Int32 blockSize)
        {
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
            var effect = new Effect<T>(library, this);
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
                Trace.WriteLine("[vst host] stop");
                foreach (var (ptr, effect) in effectMap)
                {
                    Trace.WriteLine("[vst] try stop");
                    effect.Dispose();
                }
                effectMap.Clear();
                vstTimeInfo.Dispose();
            }

            disposed = true;
        }

        private static DateTime UNIX_EPOCH = new DateTime(1970, 1, 1, 0, 0, 0, 0);

        internal IntPtr AudioMasterCallBackProc(
            IntPtr/*AEffect^*/		aeffectPtr,
            AudioMasterOpcodes opcode,
            Int32 index,
            IntPtr value,
            IntPtr ptr,
            Single opt
        )
        {
            Trace.WriteLine($"AudioMasterCallBackProc opcode:{opcode:F}");
            switch (opcode)
            {
                case AudioMasterOpcodes.audioMasterVersion:
                    {
                        return new IntPtr(2400);
                    }
                case AudioMasterOpcodes.audioMasterGetTime:
                    {
                        var now = DateTime.UtcNow;
                        var usec = ((long)(now - UNIX_EPOCH).TotalSeconds * 1000000) + (now.Millisecond * 1000);

                        vstTimeInfo.Target.nanoSeconds = usec * 1000;

                        return vstTimeInfo.AddrOfPinnedObject();
                    }
                case AudioMasterOpcodes.audioMasterGetSampleRate:
                    return new IntPtr(SamplingRate);

                case AudioMasterOpcodes.audioMasterGetBlockSize:
                    return new IntPtr(BlockSize);
            }
            return IntPtr.Zero;
        }
    }


    public class Effect<T> : IDisposable
    {
        private bool disposed = false;
        private Kernel32.DynamicLinkLibrary dll;
        public IntPtr AeffectPtr { get; private set; }

        private Task processTask;

        private AEffectDispatcherProc dispatcher;
        private AEffectSetParameterProc setParameter;
        private AEffectGetParameterProc getParameter;
        private AEffectProcessProc processReplacing;
        private AEffectProcessDoubleProc processDoubleReplacing;

        int numOutputs;
        private AudioMaster<T> audioMaster;

        public Effect(string library, AudioMaster<T> audioMaster)
        {
            this.audioMaster = audioMaster;

            dll = Kernel32.LoadLibrary(library);
            if (dll.IsInvalid)
            {
                var error = Marshal.GetHRForLastWin32Error();
                Trace.WriteLine($"LoadLibrary error:{error}");
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
                Trace.WriteLine($"GetProcAddress error:{error}");
                Marshal.ThrowExceptionForHR(error);
            }

            var vstPluginMain =
                Marshal.GetDelegateForFunctionPointer<VSTPluginMain>(proc);

            AeffectPtr = vstPluginMain(audioMaster.AudioMasterCallBackProc);
            var aeffect = Marshal.PtrToStructure<AEffect>(AeffectPtr);
            numOutputs = aeffect.numOutputs;

            Trace.WriteLine($"magic:{aeffect.magic}");
            Trace.WriteLine($"dispatcher:{aeffect.dispatcher}");
            Trace.WriteLine($"processDeprecated:{aeffect.processDeprecated}");
            Trace.WriteLine($"setParameter:{aeffect.setParameter}");
            Trace.WriteLine($"getParameter:{aeffect.getParameter}");

            Trace.WriteLine($"numPrograms:{aeffect.numPrograms}");
            Trace.WriteLine($"numParams:{aeffect.numParams}");
            Trace.WriteLine($"numInputs:{aeffect.numInputs}");
            Trace.WriteLine($"numOutputs:{aeffect.numOutputs}");
            Trace.WriteLine($"flags:{aeffect.flags}");

            //Trace.WriteLine($"resvd1:"+aeffect.resvd1);
            //Trace.WriteLine($"resvd2:"+aeffect.resvd2);

            Trace.WriteLine($"initialDelay:{aeffect.initialDelay}");

            Trace.WriteLine($"realQualitiesDeprecated:{aeffect.realQualitiesDeprecated}");
            Trace.WriteLine($"offQualitiesDeprecated:{aeffect.offQualitiesDeprecated}");
            Trace.WriteLine($"ioRatioDeprecated:{aeffect.ioRatioDeprecated}");
            //Trace.WriteLine($"object:"+aeffect._object);
            Trace.WriteLine($"user:{aeffect.user}");

            Trace.WriteLine($"uniqueID:{aeffect.uniqueID}");
            Trace.WriteLine($"version:{aeffect.version}");

            //Trace.WriteLine("processReplacing:"+aeffect.processReplacing);
            //Trace.WriteLine("processDoubleReplacing:"+aeffect.processDoubleReplacing);

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

        public void Run(
            ISourceBlock<Wave.PcmBuffer<T>> bufferQueue,
            ITargetBlock<Wave.PcmBuffer<T>> outputQueue,
            IReceivableSourceBlock<VstMidiEvent> midiEventQueue,
            CancellationToken ct)
        {
            processTask = Process(bufferQueue, outputQueue, midiEventQueue, ct);
        }

        private async Task Process(
            ISourceBlock<Wave.PcmBuffer<T>> bufferQueue,
            ITargetBlock<Wave.PcmBuffer<T>> outputQueue,
            IReceivableSourceBlock<VstMidiEvent> midiEventQueue,
            CancellationToken ct)
        {
            int blockSize = audioMaster.BlockSize;
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
                    var s = new SemaphoreSlim(1);

                    var interval = (long)(audioMaster.BlockSize / (float)audioMaster.SamplingRate * 1000.0);

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
                            //Trace.WriteLine("[vst] get data TRY");
                            var data = bufferQueue.Receive(new TimeSpan(20_000_000), ct);
                            {
                                var after = stopwatch.ElapsedMilliseconds;
                                var diff = after - before;
                                var left = interval - diff;
                                if (left > 0)
                                {
                                    //セマフォで時間調整を行う
                                    s.Wait((int)left);
                                    after = stopwatch.ElapsedMilliseconds;
                                }
                                Trace.WriteLine($"[vst] get data OK [{diff}+{left}][{interval}]");
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
                                Trace.WriteLine($"effProcessEvents:{processEventsResult}");
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
                                    target[targetIdx++] = (T)(object)left[idx];
                                    target[targetIdx++] = (T)(object)right[idx];

                                    //target[targetIdx++] = (T)(object)Convert.ToInt16(left[idx] * short.MaxValue);
                                    //target[targetIdx++] = (T)(object)Convert.ToInt16(right[idx] * short.MaxValue);
                                }
                            }

                            outputQueue.Post(data);
                            Trace.WriteLine($"[vst] post data:{data.GetHashCode()}");
                        }
                        catch (TimeoutException te)
                        {
                            Trace.WriteLine("[vst] timeout");
                            continue;
                        }
                    }
                    Trace.WriteLine("[vst] loop end");
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
            Trace.WriteLine($"effOpen:{openResult}");

            var setSampleRateResult =
                dispatcher(
                    AeffectPtr,
                    AEffectOpcodes.effSetSampleRate,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    audioMaster.SamplingRate
                );
            Trace.WriteLine($"effSetSampleRate:{setSampleRateResult}");
            var setBlockSizeResult =
                dispatcher(
                    AeffectPtr,
                    AEffectOpcodes.effSetBlockSize,
                    0,
                    new IntPtr(audioMaster.BlockSize),
                    IntPtr.Zero,
                    0
                );
            Trace.WriteLine($"effSetBlockSize:{setBlockSizeResult}");
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
            Trace.WriteLine($"effMainsChanged:{resumeResult}");
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
            Trace.WriteLine($"effStartProcess:{startProcessResult}");
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
            Trace.WriteLine($"effStopProcess:{stopProcessResult}");
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
            Trace.WriteLine($"effMainsChanged:{suspendResult}");
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
            Trace.WriteLine($"effClose:{closeResult}");

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
                Trace.WriteLine("[vst] stop");
                try
                {
                    processTask.Wait();
                }
                catch (AggregateException e)
                {
                    foreach (var v in e.InnerExceptions)
                    {
                        Trace.WriteLine($"[vst] Process Exception:{e.Message} {v.Message}");
                    }
                }
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