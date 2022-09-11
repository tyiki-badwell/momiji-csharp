using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Momiji.Core.Buffer;
using Momiji.Core.Configuration;
using Momiji.Core.Dll;
using Momiji.Core.SharedMemory;
using Momiji.Core.Timer;
using Momiji.Core.Trans;
using Momiji.Core.Wave;
using Momiji.Core.Window;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.Vst.Worker;

public class VstBridgeWorker : BackgroundService
{
    public static async Task Main(string[] args)
    {
        using var host = CreateHost(args);
        await host.RunAsync().ConfigureAwait(false);
    }

    public static IHost CreateHost(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);

        builder.UseContentRoot(AppContext.BaseDirectory);

        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddHostedService<VstBridgeWorker>();
            services.AddSingleton<IDllManager, DllManager>();
            services.AddSingleton<IRunner, Runner>();
            services.AddSingleton<IWindowManager, WindowManager>();

            services.Configure<Param>(hostContext.Configuration.GetSection(typeof(Param).FullName));
        });

        var host = builder.Build();

        return host;
    }

    public async override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"START");
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"STOP");
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private readonly ILogger _logger;
    private readonly IRunner _runner;
    private readonly IWindowManager _windowManager;

    public VstBridgeWorker(
        ILogger<VstBridgeWorker> logger, 
        IRunner runner, 
        IWindowManager windowManager, 
        IHostApplicationLifetime hostApplicationLifetime
    )
    {
        _logger = logger;
        _runner = runner;
        _windowManager = windowManager;

        hostApplicationLifetime?.ApplicationStarted.Register(() =>
        {
            logger?.LogInformation("ApplicationStarted");
        });
        hostApplicationLifetime?.ApplicationStopping.Register(() =>
        {
            logger?.LogInformation("ApplicationStopping");
        });
        hostApplicationLifetime?.ApplicationStopped.Register(() =>
        {
            logger?.LogInformation("ApplicationStopped");
        });
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var taskSet = new List<Task>
        {
            _runner.StartAsync(stoppingToken),
            _windowManager.StartAsync(stoppingToken)
        };

        await Task.WhenAll(taskSet);
    }
}

public interface IRunner
{
    Task StartAsync(CancellationToken stoppingToken);
    void Cancel();

    void OpenEditor();
    void CloseEditor();

    //void Note(MIDIMessageEvent[] midiMessage);
    //Task AcceptWebSocket(WebSocket webSocket);
}

public class Runner : IRunner, IDisposable
{
    //private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly IDllManager _dllManager;
    private readonly IWindowManager _windowManager;
    private readonly Param _param;

    private bool _disposed;
    private readonly object _sync = new();
    private CancellationTokenSource? _processCancel;
    private Task? _processTask;
    private IEffect<float>? _effect;

    public Runner(ILoggerFactory loggerFactory, IDllManager dllManager, IWindowManager windowManager, IOptions<Param> param)
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<Runner>();
        _dllManager = dllManager;
        _windowManager = windowManager;

        if (param == default)
        {
            throw new ArgumentNullException(typeof(Param).FullName);
        }

        _param = param.Value;

        _logger.LogInformation($"BufferCount:{_param.BufferCount}");
        _logger.LogInformation($"Local:{_param.Local}");
        _logger.LogInformation($"Connect:{_param.Connect}");
        _logger.LogInformation($"Width:{_param.Width}");
        _logger.LogInformation($"Height:{_param.Height}");
        _logger.LogInformation($"TargetBitrate:{_param.TargetBitrate}");
        _logger.LogInformation($"MaxFrameRate:{_param.MaxFrameRate}");
        _logger.LogInformation($"IntraFrameIntervalUs:{_param.IntraFrameIntervalUs}");
        _logger.LogInformation($"EffectName:{_param.EffectName}");
        _logger.LogInformation($"SamplingRate:{_param.SamplingRate}");
        _logger.LogInformation($"SampleLength:{_param.SampleLength}");
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
            Cancel();
        }
        _disposed = true;
    }

    public async Task StartAsync(CancellationToken stoppingToken)
    {
        if (_processCancel != null)
        {
            _logger.LogInformation("[worker] already started.");
            return;
        }
        _processCancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _processTask = Run();
        await _processTask.ConfigureAwait(false);
    }

    private async Task Run()
    {
        if (_processCancel == null)
        {
            throw new InvalidOperationException($"{nameof(_processCancel)} is null.");
        }
        if (_param.EffectName == null)
        {
            throw new InvalidOperationException($"{nameof(_param.EffectName)} is null.");
        }

        var ct = _processCancel.Token;

        var taskSet = new HashSet<Task>();

        var blockSize = (int)(_param.SamplingRate * _param.SampleLength);
        var audioInterval = (long)(10_000_000.0 * _param.SampleLength);

        using var buf = new IPCBuffer<float>(_param.EffectName, blockSize * 2 * _param.BufferCount, _loggerFactory);
        //            using var vstBufferPool = new BufferPool<VstBuffer<float>>(param.BufferCount, () => new VstBuffer<float>(blockSize, 2), LoggerFactory);
        using var vstBufferPool = new BufferPool<VstBuffer2<float>>(_param.BufferCount, () => new VstBuffer2<float>(blockSize, 2, buf), _loggerFactory);
        using var pcmPool = new BufferPool<PcmBuffer<float>>(_param.BufferCount, () => new PcmBuffer<float>(blockSize, 2), _loggerFactory);
        var counter = new ElapsedTimeCounter();
        using var audioWaiter = new Waiter(counter, audioInterval, true);
        using var vst = new AudioMaster<float>(_param.SamplingRate, blockSize, _loggerFactory, counter, _dllManager, _windowManager);
        using var toPcm = new ToPcm<float>(_loggerFactory, counter);

        _logger.LogInformation($"AddEffect:{_param.EffectName}");

        _effect = vst.AddEffect(_param.EffectName);

        using var wave = new WaveOutFloat(
            0,
            2,
            _param.SamplingRate,
            SPEAKER.FrontLeft | SPEAKER.FrontRight,
            _loggerFactory,
            counter,
            pcmPool);

        var options = new ExecutionDataflowBlockOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = 1
        };

        var audioStartBlock =
            new TransformBlock<VstBuffer2<float>, VstBuffer2<float>>(buffer => {
                buffer.Log.Clear();
                var r = audioWaiter.Wait();
                if (r > 1)
                {
                    _logger.LogError($"Delay {r}");
                }
                return buffer;
            }, options);
        taskSet.Add(audioStartBlock.Completion);
        vstBufferPool.LinkTo(audioStartBlock);

        var vstBlock =
            new TransformBlock<VstBuffer2<float>, PcmBuffer<float>>(buffer =>
            {
                //VST
                var nowTime = counter.NowTicks / 10;
                _effect.ProcessReplacing(nowTime, buffer);

                //trans
                var pcm = pcmPool.Receive();
                toPcm.Execute(buffer, pcm);
                vstBufferPool.Post(buffer);

                return pcm;
            }, options);
        taskSet.Add(vstBlock.Completion);
        audioStartBlock.LinkTo(vstBlock);

        var waveBlock =
            new ActionBlock<PcmBuffer<float>>(buffer =>
            {
                //WAVEOUT
                wave.Execute(buffer, ct);
            }, options);
        taskSet.Add(waveBlock.Completion);
        vstBlock.LinkTo(waveBlock);

        while (taskSet.Count > 0)
        {
            var task = await Task.WhenAny(taskSet).ConfigureAwait(false);
            taskSet.Remove(task);
            if (task.IsFaulted)
            {
                _processCancel.Cancel();
                _logger.LogError(task.Exception, "Process Exception");
            }
        }
    }

    public void Cancel()
    {
        lock(_sync)
        {
            if (_processCancel == null)
            {
                _logger.LogInformation("[worker] already stopped.");
                return;
            }

            try
            {
                _processCancel.Cancel();
                _processTask?.Wait();
            }
            catch (AggregateException e)
            {
                _logger.LogInformation(e, "[worker] Process Cancel Exception");
            }
            finally
            {
                _processCancel.Dispose();
                _processCancel = null;

                _processTask?.Dispose();
                _processTask = null;
            }
            _logger.LogInformation("[worker] stopped.");
        }
    }

    public void OpenEditor()
    {
        if (_effect == default)
        {
            throw new InvalidOperationException($"{nameof(_effect)} is null.");
        }
        if (_processCancel == default)
        {
            throw new InvalidOperationException($"{nameof(_processCancel)} is null.");
        }
        _effect.OpenEditor(_processCancel.Token);
    }

    public void CloseEditor()
    {
        if (_effect == default)
        {
            throw new InvalidOperationException($"{nameof(_effect)} is null.");
        }

        _effect.CloseEditor();
    }
}
