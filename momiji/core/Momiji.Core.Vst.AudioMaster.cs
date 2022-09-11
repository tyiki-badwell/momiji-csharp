using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Momiji.Core.Dll;
using Momiji.Core.SharedMemory;
using Momiji.Core.Timer;
using Momiji.Core.Window;
using Momiji.Interop.Vst;
using Momiji.Interop.Vst.AudioMaster;

namespace Momiji.Core.Vst;

public class AudioMaster<T> : IDisposable where T : struct
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly ElapsedTimeCounter _counter;
    internal IDllManager DllManager { get; }
    internal IWindowManager WindowManager { get; }

    private bool _disposed;
    internal IDictionary<IntPtr, Effect<T>> EffectMap { get; } = new ConcurrentDictionary<IntPtr, Effect<T>>();

    private readonly IPCBuffer<VstHostParam> _param;

    public int SamplingRate {
        get
        {
            var p = _param.AsSpan(0, 1);
            return p[0].samplingRate;
        }
    }
    public int BlockSize
    {
        get
        {
            var p = _param.AsSpan(0, 1);
            return p[0].blockSize;
        }
    }

    public AudioMaster(
        int samplingRate,
        int blockSize,
        ILoggerFactory loggerFactory,
        ElapsedTimeCounter counter,
        IDllManager dllManager,
        IWindowManager windowManager
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<AudioMaster<T>>();
        _counter = counter;
        DllManager = dllManager;
        WindowManager = windowManager;

        _param = new("vstTimeInfo", 1, _loggerFactory);
        var p = _param.AsSpan(0, 1);
        p[0].vstTimeInfo.samplePos = 0.0;
        p[0].vstTimeInfo.sampleRate = samplingRate;
        p[0].vstTimeInfo.nanoSeconds = 0.0;
        p[0].vstTimeInfo.ppqPos = 0.0;
        p[0].vstTimeInfo.tempo = 240.0;
        p[0].vstTimeInfo.barStartPos = 0.0;
        p[0].vstTimeInfo.cycleStartPos = 0.0;
        p[0].vstTimeInfo.cycleEndPos = 0.0;
        p[0].vstTimeInfo.timeSigNumerator = 4;
        p[0].vstTimeInfo.timeSigDenominator = 4;
        p[0].vstTimeInfo.smpteOffset = 0;
        p[0].vstTimeInfo.smpteFrameRate = VstTimeInfo.VstSmpteFrameRate.kVstSmpte24fps;
        p[0].vstTimeInfo.samplesToNextClock = 0;
        p[0].vstTimeInfo.flags = VstTimeInfo.VstTimeInfoFlags.kVstTempoValid | VstTimeInfo.VstTimeInfoFlags.kVstNanosValid;

        p[0].vstProcessLevels = VstProcessLevels.kVstProcessLevelRealtime;
        p[0].samplingRate = samplingRate;
        p[0].blockSize = blockSize;
    }

    ~AudioMaster()
    {
        Dispose(false);
    }

    public IEffect<T> AddEffect(string? library)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (library.Length == 0)
        {
            throw new ArgumentNullException(nameof(library));
        }

        return new Effect<T>(library, this, _loggerFactory, _counter);
    }

    public void RemoveEffect(IEffect<T> effect)
    {
        var e = effect as Effect<T>;
        e?.Dispose();
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
            _logger.LogInformation($"[vst host] stop : opened effect [{EffectMap.Count}]");
            foreach (var (ptr, effect) in EffectMap)
            {
                _logger.LogInformation($"[vst] try stop [{ptr}]");
                effect.Dispose();
            }
            _logger.LogInformation($"[vst host] left [{EffectMap.Count}]");

            EffectMap.Clear();
            _param.Dispose();
        }

        _disposed = true;
    }

    internal IntPtr AudioMasterCallBackProc(
        IntPtr/*AEffect^*/		aeffectPtr,
        Opcodes opcode,
        int index,
        IntPtr value,
        IntPtr ptr,
        float opt
    )
    {
        /*
        Logger.LogInformation(
            $"AudioMasterCallBackProc " +
            $"{nameof(aeffectPtr)}:{aeffectPtr:X} " +
            $"{nameof(opcode)}:{opcode:F} " +
            $"{nameof(index)}:{index} " +
            $"{nameof(value)}:{value:X} " +
            $"{nameof(ptr)}:{ptr:X} " +
            $"{nameof(opt)}:{opt}"
        );
        */

        switch (opcode)
        {
            case Opcodes.audioMasterVersion:
                {
                    return new IntPtr(2400);
                }
            case Opcodes.audioMasterGetTime:
                {
                    var p = _param.AsSpan(0, 1);
                    p[0].vstTimeInfo.nanoSeconds = _counter.NowTicks * 100;
                    return _param.GetIntPtr(0);
                }
            case Opcodes.audioMasterGetSampleRate:
                {
                    return new IntPtr(SamplingRate);
                }
            case Opcodes.audioMasterGetBlockSize:
                {
                    return new IntPtr(BlockSize);
                }
            case Opcodes.audioMasterGetCurrentProcessLevel:
                {
                    var p = _param.AsSpan(0, 1);
                    return new IntPtr((int)p[0].vstProcessLevels);
                }

            default:
                //Logger.LogInformation("NOP");
                return default;
        }
    }
}

