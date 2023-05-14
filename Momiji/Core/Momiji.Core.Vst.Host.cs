using Microsoft.Extensions.Logging;
using Momiji.Core.SharedMemory;
using Momiji.Interop.Vst;
using Momiji.Interop.Vst.AudioMaster;

namespace Momiji.Core.Vst;

public class VstHostException : Exception
{
    public VstHostException()
    {
    }

    public VstHostException(string message) : base(message)
    {
    }

    public VstHostException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

internal struct VstHostParam
{
    public VstTimeInfo vstTimeInfo;
    public VstProcessLevels vstProcessLevels;
    public int samplingRate;
    public int blockSize;
}

public class VstHost : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    private bool _disposed;

    private readonly IPCBuffer<VstHostParam> _param;

    private readonly IList<EffectProxy> _effectList = new List<EffectProxy>();

    public int SamplingRate
    {
        get
        {
            if (_param == null)
            {
                throw new InvalidOperationException("param is null.");
            }

            var p = _param.AsSpan(0, 1);
            return p[0].samplingRate;
        }

        set
        {
            if (_param == null)
            {
                throw new InvalidOperationException("param is null.");
            }

            //TODO 動作中はエラー
            var p = _param.AsSpan(0, 1);
            p[0].samplingRate = value;
        }

    }
    public int BlockSize
    {
        get
        {
            if (_param == null)
            {
                throw new InvalidOperationException("param is null.");
            }

            var p = _param.AsSpan(0, 1);
            return p[0].blockSize;
        }

        set
        {
            if (_param == null)
            {
                throw new InvalidOperationException("param is null.");
            }

            //TODO 動作中はエラー
            var p = _param.AsSpan(0, 1);
            p[0].blockSize = value;
        }
    }

    public VstHost(
        int samplingRate,
        int blockSize,
        ILoggerFactory loggerFactory
    )
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = _loggerFactory.CreateLogger<VstHost>();

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

    ~VstHost()
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
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            foreach (var effect in _effectList)
            {
                effect.Dispose();
            }
            _effectList.Clear();

            _param?.Dispose();

            _logger.LogInformation($"[vst host] disposing");
        }

        _disposed = true;
    }

    public EffectProxy AddEffect(string library, bool is64)
    {
        var effect = new EffectProxy(this, library, is64, _loggerFactory);
        _effectList.Add(effect);
        return effect;
    }


    public void Start()
    {
        foreach (var effect in _effectList)
        {
            effect.Start();
        }
    }

    public void Stop()
    {
        foreach (var effect in _effectList)
        {
            effect.Stop();
        }
    }

}


public class EffectProxy : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    private readonly VstHost _parent;

    private bool _disposed;

    public EffectProxy(
        VstHost vstHost,
        string library,
        bool is64,
        ILoggerFactory loggerFactory
    )
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = _loggerFactory.CreateLogger<EffectProxy>();
        _parent = vstHost;





    }

    ~EffectProxy()
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
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger.LogInformation($"[vst effect proxy] disposing");
        }



        _disposed = true;
    }

    internal void Start()
    {

    }

    internal void Stop()
    {

    }
}
