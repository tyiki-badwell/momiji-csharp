using Momiji.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji
{
    namespace Core
    {
        public class Vst
        {
            public class VstBuffer<T> : PinnedBuffer<IntPtr[]>
            {
                private bool disposed = false;
                private List<PinnedBuffer<T[]>> list = new List<PinnedBuffer<T[]>>();

                public VstBuffer(int blockSize, int channels): base(new IntPtr[channels])
                {
                    for (var i = 0; i < channels; i++)
                    {
                        var buffer = new PinnedBuffer<T[]>(new T[blockSize]);
                        list.Add(buffer);
                        Target()[i] = buffer.AddrOfPinnedObject();
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
                    return list[index].Target();
                }
            }

            public class AudioMaster : IDisposable
            {
                private bool disposed = false;
                private List<Effect> list = new List<Effect>();
                private PinnedBuffer<Interop.Vst.VstTimeInfo> vstTimeInfo;

                public int SamplingRate { get; }
                public int BlockSize { get; }

                public AudioMaster(int samplingRate, int blockSize)
                {
                    SamplingRate = samplingRate;
                    BlockSize = blockSize;

                    var timeInfo = new Interop.Vst.VstTimeInfo();
                    vstTimeInfo = new PinnedBuffer<Interop.Vst.VstTimeInfo>(timeInfo);

                    timeInfo.samplePos = 0.0;
                    timeInfo.sampleRate = 0;
                    timeInfo.nanoSeconds = 0.0;
                    timeInfo.ppqPos = 0.0;
                    timeInfo.tempo = 240.0;
                    timeInfo.barStartPos = 0.0;
                    timeInfo.cycleStartPos = 0.0;
                    timeInfo.cycleEndPos = 0.0;
                    timeInfo.timeSigNumerator = 4;
                    timeInfo.timeSigDenominator = 4;
                    timeInfo.smpteOffset = 0;
                    timeInfo.smpteFrameRate = 1;
                    timeInfo.samplesToNextClock = 0;
                    timeInfo.flags = Interop.Vst.VstTimeInfo.VstTimeInfoFlags.kVstTempoValid;
                }

                public Effect AddEffect(string library)
                {
                    var effect = new Effect(library, this);
                    list.Add(effect);
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
                        list.ForEach(effect => { effect.Dispose(); });
                    }

                    disposed = true;
                }

                internal IntPtr AudioMasterCallBackProc(
                    IntPtr/*AEffect^*/		aeffectPtr,
                    Interop.Vst.AudioMasterOpcodes opcode,
                    Int32 index,
                    IntPtr value,
                    IntPtr ptr,
                    Single opt
                )
                {
                    Trace.WriteLine($"AudioMasterCallBackProc opcode:{opcode}");
                    switch (opcode)
                    {
                        case Interop.Vst.AudioMasterOpcodes.audioMasterVersion:
                            {
                                return new IntPtr(2400);
                            }
                        case Interop.Vst.AudioMasterOpcodes.audioMasterGetTime:
                            {
                                return vstTimeInfo.AddrOfPinnedObject();
                            }
                        case Interop.Vst.AudioMasterOpcodes.audioMasterGetSampleRate:
                            return new IntPtr(SamplingRate);

                        case Interop.Vst.AudioMasterOpcodes.audioMasterGetBlockSize:
                            return new IntPtr(BlockSize);
                    }
                    return IntPtr.Zero;
                }
            }


            public class Effect : IDisposable
            {
                private bool disposed = false;
                private Kernel32.DynamicLinkLibrary dll;
                private IntPtr aeffectPtr;

                private CancellationTokenSource processCancel = new CancellationTokenSource();
                private Task processTask;

                private Interop.Vst.AEffectDispatcherProc dispatcher;
                private Interop.Vst.AEffectSetParameterProc setParameter;
                private Interop.Vst.AEffectGetParameterProc getParameter;
                private Interop.Vst.AEffectProcessProc processReplacing;
                private Interop.Vst.AEffectProcessDoubleProc processDoubleReplacing;

                private AudioMaster audioMaster;
                int numOutputs;

                public Effect(string library, AudioMaster audioMaster)
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
                        Marshal.GetDelegateForFunctionPointer<Interop.Vst.VSTPluginMain>(proc);

                    aeffectPtr = vstPluginMain(audioMaster.AudioMasterCallBackProc);
                    var aeffect =
                        Marshal.PtrToStructure<Interop.Vst.AEffect>(aeffectPtr);
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
                            Marshal.GetDelegateForFunctionPointer<Interop.Vst.AEffectDispatcherProc>(aeffect.dispatcher);
                    }

                    if (aeffect.setParameter != IntPtr.Zero)
                    {
                        setParameter =
                            Marshal.GetDelegateForFunctionPointer<Interop.Vst.AEffectSetParameterProc>(aeffect.setParameter);
                    }

                    if (aeffect.getParameter != IntPtr.Zero)
                    {
                        getParameter =
                            Marshal.GetDelegateForFunctionPointer<Interop.Vst.AEffectGetParameterProc>(aeffect.getParameter);
                    }

                    if (aeffect.processReplacing != IntPtr.Zero)
                    {
                        processReplacing =
                            Marshal.GetDelegateForFunctionPointer<Interop.Vst.AEffectProcessProc>(aeffect.processReplacing);
                    }

                    if (aeffect.processDoubleReplacing != IntPtr.Zero)
                    {
                        processDoubleReplacing =
                            Marshal.GetDelegateForFunctionPointer<Interop.Vst.AEffectProcessDoubleProc>(aeffect.processDoubleReplacing);
                    }

                    Open(audioMaster);
                }

                public void Run(
                    ISourceBlock<PinnedBuffer<float[]>> bufferQueue,
                    ITargetBlock<PinnedBuffer<float[]>> outputQueue)
                {
                    processTask = Process(bufferQueue, outputQueue);
                }

                private async Task Process(
                    ISourceBlock<PinnedBuffer<float[]>> bufferQueue,
                    ITargetBlock<PinnedBuffer<float[]>> outputQueue)
                {
                    var ct = processCancel.Token;
                    var blockSize = audioMaster.BlockSize;

                    using (var buffer = new VstBuffer<float>(blockSize, numOutputs))
                    {
                        await Task.Run(() =>
                        {
                            ct.ThrowIfCancellationRequested();

                            while (true)
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    break;
                                }

                                try
                                { 
                                    var data = bufferQueue.Receive(new TimeSpan(1000), ct);
                                    Trace.WriteLine("[vst] get data");

                                    processReplacing(
                                        aeffectPtr,
                                        IntPtr.Zero,
                                        buffer.AddrOfPinnedObject(),
                                        audioMaster.BlockSize
                                    );

                                    var target = data.Target();
                                    var targetIdx = 0;
                                    var left = buffer.Get(0);
                                    var right = buffer.Get(1);

                                    for (var idx = 0; idx < blockSize; idx++)
                                    {
                                        target[targetIdx++] = left[idx];
                                        target[targetIdx++] = right[idx];
                                    }

                                    outputQueue.Post(data);
                                    Trace.WriteLine("[vst] post data");
                                }
                                catch (TimeoutException te)
                                {
                                    Trace.WriteLine("[vst] timeout");
                                    continue;
                                }
                            }
                        });
                    }
                }

                private void Open(AudioMaster audioMaster)
                {
                    var openResult =
                        dispatcher(
                            aeffectPtr,
                            Interop.Vst.AEffectOpcodes.effOpen,
                            0,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            0
                        );
                    Trace.WriteLine($"effOpen:{openResult}");

                    var setSampleRateResult =
                        dispatcher(
                            aeffectPtr,
                            Interop.Vst.AEffectOpcodes.effSetSampleRate,
                            0,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            audioMaster.SamplingRate
                        );
                    Trace.WriteLine($"effSetSampleRate:{setSampleRateResult}");
                    var setBlockSizeResult =
                        dispatcher(
                            aeffectPtr,
                            Interop.Vst.AEffectOpcodes.effSetBlockSize,
                            0,
                            new IntPtr(audioMaster.BlockSize),
                            IntPtr.Zero,
                            0
                        );
                    Trace.WriteLine($"effSetBlockSize:{setBlockSizeResult}");
                    //resume
                    var resumeResult =
                        dispatcher(
                            aeffectPtr,
                            Interop.Vst.AEffectOpcodes.effMainsChanged,
                            0,
                            new IntPtr(1),
                            IntPtr.Zero,
                            0
                        );
                    Trace.WriteLine($"effMainsChanged:{resumeResult}");
                    //start
                    var startProcessResult =
                        dispatcher(
                            aeffectPtr,
                            Interop.Vst.AEffectOpcodes.effStartProcess,
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
                            aeffectPtr,
                            Interop.Vst.AEffectOpcodes.effStopProcess,
                            0,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            0
                        );
                    Trace.WriteLine($"effStopProcess:{stopProcessResult}");
                    //suspend
                    var suspendResult =
                        dispatcher(
                            aeffectPtr,
                            Interop.Vst.AEffectOpcodes.effMainsChanged,
                            0,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            0
                        );
                    Trace.WriteLine($"effMainsChanged:{suspendResult}");
                    //close
                    var closeResult =
                        dispatcher(
                            aeffectPtr,
                            Interop.Vst.AEffectOpcodes.effClose,
                            0,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            0
                        );
                    Trace.WriteLine($"effClose:{closeResult}");

                    aeffectPtr = IntPtr.Zero;
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
                        processCancel.Cancel();
                        try
                        {
                            processTask.Wait();
                        }
                        catch (AggregateException e)
                        {
                            foreach (var v in e.InnerExceptions)
                            {
                                Trace.WriteLine($"FtlIngest Process Exception:{e.Message} {v.Message}");
                            }
                        }
                        finally
                        {
                            processCancel.Dispose();
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
    }
}
