using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Core.SharedMemory;
using Momiji.Core.Timer;
using Momiji.Core.WebMidi;
using Momiji.Core.Window;
using Momiji.Interop.Vst;
using Momiji.Interop.Vst.AudioMaster;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.Vst;

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
    private bool _disposed;
    internal PinnedBuffer<IntPtr[]> Buffer { get; }
    private readonly List<PinnedBuffer<T[]>> _list = new();
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
            _list.Add(buffer);
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
        if (_disposed) return;

        if (disposing)
        {
            _list.ForEach(buffer => buffer.Dispose());
            _list.Clear();
        }

        Buffer?.Dispose();
        _disposed = true;
    }

    public T[] GetChannelBuffer(int channel) => _list[channel].Target;
}

public class VstBuffer2<T> : IDisposable where T : struct
{
    private bool _disposed;

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
        if (_disposed) return;

        if (disposing)
        {
        }

        Buffer?.Dispose();
        _disposed = true;
    }
    public IntPtr GetChannelBuffer(int channel) => Buffer.Target[channel];
}

public interface IEffect<T> where T : struct
{
    void ProcessReplacing(
        long nowTime,
        VstBuffer<T> source
    );

    void ProcessReplacing(
        long nowTime,
        VstBuffer2<T> source
    );
    void ProcessEvent(
        long nowTime,
        IReceivableSourceBlock<MIDIMessageEvent2> midiEventInput,
        ITargetBlock<MIDIMessageEvent2>? midiEventOutput = null
    );
    void OpenEditor(CancellationToken cancellationToken);
    Task CloseEditorAsync();
}

internal class Effect<T> : IEffect<T>, IDisposable where T : struct
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly ElapsedTimeCounter _counter;

    private bool _disposed;

    private readonly IntPtr _aeffectPtr;

    private NativeWindow? _window;
    private Task? _windowTask;

    //private Bitmap bitmap;
    internal ERect EditorRect { get; private set; }

    private readonly AEffect.DispatcherProc? _dispatcherProc;
    private readonly AEffect.SetParameterProc? _setParameterProc;
    private readonly AEffect.GetParameterProc? _getParameterProc;
    private readonly AEffect.ProcessProc? _processProc;

    private readonly AudioMaster<T> _audioMaster;
    private readonly PinnedDelegate<CallBack> _audioMasterCallBack;

    private readonly PinnedBuffer<byte[]> _events;
    private readonly PinnedBuffer<byte[]> _eventList;

    private long _beforeTime;
    private MIDIMessageEvent2? _extraMidiEvent;

    private static readonly int SIZE_OF_VSTEVENTS = Marshal.SizeOf<VstEvents>();
    private static readonly int SIZE_OF_VSTMIDIEVENT = Marshal.SizeOf<VstMidiEvent>();
    private static readonly int SIZE_OF_INTPTR = Marshal.SizeOf<IntPtr>();
    private static readonly int SIZE_OF_T = Marshal.SizeOf<T>();
    private const int COUNT_OF_EVENTS = 500; //サイズが適当

    internal Effect(
        string library,
        AudioMaster<T> audioMaster,
        ILoggerFactory loggerFactory,
        ElapsedTimeCounter counter
    )
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));

        _logger = _loggerFactory.CreateLogger<Effect<T>>();

        _audioMaster = audioMaster;

        //TODO IPCBufferにする
        _events = new(new byte[SIZE_OF_VSTEVENTS + (SIZE_OF_INTPTR * COUNT_OF_EVENTS)]);
        _eventList = new(new byte[SIZE_OF_VSTMIDIEVENT * COUNT_OF_EVENTS]);

        var vstPluginMain = _audioMaster.DllManager.GetExport<AEffect.VSTPluginMain>(library, "VSTPluginMain");
        if (vstPluginMain == default)
        {
            vstPluginMain = _audioMaster.DllManager.GetExport<AEffect.VSTPluginMain>(library, "main");
        }

        if (vstPluginMain == default)
        {
            throw new VstException("VSTPluginMainが取得できない");
        }

        _audioMasterCallBack = new(new(_audioMaster.AudioMasterCallBackProc));
        _aeffectPtr = vstPluginMain(_audioMasterCallBack.FunctionPointer);
        if (_aeffectPtr == default)
        {
            throw new VstException("vstPluginMain で失敗");
        }

        var aeffect = GetAEffect();

        _logger.LogInformation($"magic:{aeffect.magic}");
        _logger.LogInformation($"dispatcher:{aeffect.dispatcher}");
        _logger.LogInformation($"processDeprecated:{aeffect.processDeprecated}");
        _logger.LogInformation($"setParameter:{aeffect.setParameter}");
        _logger.LogInformation($"getParameter:{aeffect.getParameter}");

        _logger.LogInformation($"numPrograms:{aeffect.numPrograms}");
        _logger.LogInformation($"numParams:{aeffect.numParams}");
        _logger.LogInformation($"numInputs:{aeffect.numInputs}");
        _logger.LogInformation($"numOutputs:{aeffect.numOutputs}");
        _logger.LogInformation($"flags:{aeffect.flags}");

        //Logger.LogInformation($"resvd1:"+aeffect.resvd1);
        //Logger.LogInformation($"resvd2:"+aeffect.resvd2);

        _logger.LogInformation($"initialDelay:{aeffect.initialDelay}");

        _logger.LogInformation($"realQualitiesDeprecated:{aeffect.realQualitiesDeprecated}");
        _logger.LogInformation($"offQualitiesDeprecated:{aeffect.offQualitiesDeprecated}");
        _logger.LogInformation($"ioRatioDeprecated:{aeffect.ioRatioDeprecated}");
        //Logger.LogInformation($"object:"+aeffect._object);
        _logger.LogInformation($"user:{aeffect.user}");

        _logger.LogInformation($"uniqueID:{aeffect.uniqueID}");
        _logger.LogInformation($"version:{aeffect.version}");

        _logger.LogInformation("processReplacing:" + aeffect.processReplacing);
        _logger.LogInformation("processDoubleReplacing:" + aeffect.processDoubleReplacing);

        if (aeffect.dispatcher != default)
        {
            _dispatcherProc =
                Marshal.GetDelegateForFunctionPointer<AEffect.DispatcherProc>(aeffect.dispatcher);
        }

        if (aeffect.setParameter != default)
        {
            _setParameterProc =
                Marshal.GetDelegateForFunctionPointer<AEffect.SetParameterProc>(aeffect.setParameter);
        }

        if (aeffect.getParameter != default)
        {
            _getParameterProc =
                Marshal.GetDelegateForFunctionPointer<AEffect.GetParameterProc>(aeffect.getParameter);
        }

        if (SIZE_OF_T == 4)
        {
            if (aeffect.processReplacing != default)
            {
                _processProc =
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
                _processProc =
                    Marshal.GetDelegateForFunctionPointer<AEffect.ProcessProc>(aeffect.processDoubleReplacing);
            }
            else
            {
                throw new VstException("processDoubleReplacing が無い");
            }
        }

        Open();

        _beforeTime = _counter.NowTicks;

        _audioMaster.EffectMap.Add(_aeffectPtr, this);
    }

    ~Effect()
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
        if (_disposed) return;

        if (disposing)
        {
            _logger.LogInformation("[vst] stop");

            CloseAsync().Wait();
            _audioMaster.EffectMap.Remove(_aeffectPtr);

            _audioMasterCallBack?.Dispose();
            _events?.Dispose();
            _eventList?.Dispose();
        }

        _disposed = true;
    }

    internal ref AEffect GetAEffect()
    {
        unsafe
        {
            return ref Unsafe.AsRef<AEffect>((void*)_aeffectPtr);
        }
    }

    public float GetParameter(int index)
    {
        if (_getParameterProc == default)
        {
            return default;
        }
        return _getParameterProc(_aeffectPtr, index);
    }

    public void SetParameter(int index, float value)
    {
        if (_setParameterProc == default)
        {
            return;
        }
        _setParameterProc(_aeffectPtr, index, value);
    }

    private IntPtr Dispatcher(
        AEffect.Opcodes opcode,
        int index,
        IntPtr value,
        IntPtr ptr,
        float opt
    )
    {
        if (_dispatcherProc == default)
        {
            return default;
        }

        _logger.LogInformation($"[vst] DispatcherProc({opcode},{index},{value},{ptr},{opt})");
        try
        {
            return _dispatcherProc(
                _aeffectPtr,
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
            _logger.LogError(e, $"[vst] DispatcherProc Exception({opcode},{index},{value},{ptr},{opt})");
            return IntPtr.Zero;
        }
    }

    private string? GetString(AEffect.Opcodes opcode, int index, int length)
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
    public string? GetParameterLabel(int index)
    {
        return GetString(AEffect.Opcodes.effGetParamLabel, index, 100);
    }
    public string? GetParameterName(int index)
    {
        return GetString(AEffect.Opcodes.effGetParamName, index, 100);
    }
    public string? GetParameterDisplay(int index)
    {
        return GetString(AEffect.Opcodes.effGetParamDisplay, index, 100);
    }

    public void ProcessReplacing(
        long nowTime,
        VstBuffer<T> source
    )
    {
        if (_processProc == default)
        {
            return;
        }
        if (source == default)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var blockSize = source.BlockSize;

        source.Log.Add("[vst] start processReplacing", _counter.NowTicks);
        try
        {
            _processProc(
                _aeffectPtr,
                default,
                source.Buffer.AddrOfPinnedObject,
                blockSize
            );
            source.Log.Add("[vst] end processReplacing", _counter.NowTicks);
            _beforeTime = nowTime;
        }
#pragma warning disable CA1031 // 一般的な例外の種類はキャッチしません
        catch (Exception e)
#pragma warning restore CA1031 // 一般的な例外の種類はキャッチしません
        {
            _logger.LogError(e, $"[vst] ProcessProc Exception");
        }
    }

    public void ProcessReplacing(
        long nowTime,
        VstBuffer2<T> source
    )
    {
        if (_processProc == default)
        {
            return;
        }
        if (source == default)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var blockSize = source.BlockSize;

        source.Log.Add("[vst] start processReplacing", _counter.NowTicks);
        try
        {
            _processProc(
                _aeffectPtr,
                default,
                source.Buffer.AddrOfPinnedObject,
                blockSize
            );
            source.Log.Add("[vst] end processReplacing", _counter.NowTicks);
            _beforeTime = nowTime;
        }
#pragma warning disable CA1031 // 一般的な例外の種類はキャッチしません
        catch (Exception e)
#pragma warning restore CA1031 // 一般的な例外の種類はキャッチしません
        {
            _logger.LogError(e, $"[vst] ProcessProc Exception");
        }
    }

    public void ProcessEvent(
        long nowTime,
        IReceivableSourceBlock<MIDIMessageEvent2> midiEventInput,
        ITargetBlock<MIDIMessageEvent2>? midiEventOutput = null
    )
    {
        if (_events == default)
        {
            throw new InvalidOperationException("events is null.");
        }
        if (_eventList == default)
        {
            throw new InvalidOperationException("eventList is null.");
        }

        var samplingRate = _audioMaster.SamplingRate;

        var list = new List<MIDIMessageEvent2>();
        if (_extraMidiEvent.HasValue)
        {
            //前回の余分なイベントをここで回収
            list.Add(_extraMidiEvent.Value);
            midiEventOutput?.Post(_extraMidiEvent.Value);
            _extraMidiEvent = null;
        }

        {
            //TODO この一帯がかなり遅い
            while (midiEventInput.TryReceive(out MIDIMessageEvent2 item))
            {
                if ((item.midiMessageEvent.receivedTime * 1000) > nowTime)
                {
                    //処理している間にもイベントが増えているので、取りすぎたら次回に回す
                    _extraMidiEvent = item;
                    break;
                }
                list.Add(item);
                midiEventOutput?.Post(item);
            }
        }

        if (list.Count > 0)
        {
            var eventsPtr = _events.AddrOfPinnedObject;
            var eventListPtr = _eventList.AddrOfPinnedObject;

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
                var deltaTime = (midiEvent.midiMessageEvent.receivedTime * 1000) - _beforeTime;
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
                _events.AddrOfPinnedObject,
                default
            );
        }
    }
    private void Open()
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
            _audioMaster.SamplingRate
        );

        // set block size
        Dispatcher(
            AEffect.Opcodes.effSetBlockSize,
            default,
            new IntPtr(_audioMaster.BlockSize),
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
            _logger.LogInformation("[vst] effFlagsHasEditorではない");
            return;
        }

        if (_window != default)
        {
            _logger.LogInformation("[vst] Editorが起動済");
            return;
        }

        _window = new NativeWindow(_loggerFactory, OnCreateWindow, OnPreCloseWindow, OnPostPaint);

        _windowTask = _window.RunAsync(cancellationToken);
        _logger.LogInformation("[vst] Editor Open");
        _ = _windowTask.ContinueWith((task) =>
        {
            _logger.LogInformation("[vst] Editor End");
            _window = default;
            _windowTask = default;
        }, TaskScheduler.Default).ConfigureAwait(false);
    }

    private void OnCreateWindow(HandleRef hWindow, ref int width, ref int height)
    {
        {
            _logger.LogInformation($"[vst] open call back current {Environment.CurrentManagedThreadId:X}");

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
                _logger.LogInformation("[vst] effEditOpen failed");
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
                _logger.LogInformation("[vst] effEditGetRect failed");
            }

            EditorRect = Marshal.PtrToStructure<ERect>(buffer.Target);
            _logger.LogInformation($"[vst] effEditGetRect width;{EditorRect.right - EditorRect.left} height:{EditorRect.bottom - EditorRect.top}");

            width = EditorRect.right - EditorRect.left;
            height = EditorRect.bottom - EditorRect.top;

            //キャプチャ先を作成
            //bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        }
    }

    private void OnPreCloseWindow()
    {
        _logger.LogInformation($"[vst] close call back current {Environment.CurrentManagedThreadId:X}");

        {
            var _ =
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

    public async Task CloseEditorAsync()
    {
        var aeffect = GetAEffect();
        if (!aeffect.flags.HasFlag(AEffect.VstAEffectFlags.effFlagsHasEditor))
        {
            _logger.LogInformation("[vst] effFlagsHasEditorではない");
            return;
        }

        if (_window == default)
        {
            return;
        }

        _window.Close();

        if (_windowTask == default)
        {
            return;
        }

        try
        {
            await _windowTask.ConfigureAwait(false);
        }
        catch (WindowException e)
        {
            _logger.LogError(e, "[vst] Editor error");
        }
        _logger.LogInformation("[vst] Editor Closed");
    }

    private async Task CloseAsync()
    {
        await CloseEditorAsync().ConfigureAwait(false);

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

}
