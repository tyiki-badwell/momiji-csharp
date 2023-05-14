using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Momiji.Core.Buffer;
using Momiji.Core.Configuration;
using Momiji.Core.Dll;
using Momiji.Core.RTWorkQueue;
using Momiji.Core.RTWorkQueue.Tasks;
using Momiji.Core.SharedMemory;
using Momiji.Core.Timer;
using Momiji.Core.Trans;
using Momiji.Core.Wave;
using Momiji.Core.WebMidi;
using Momiji.Core.Window;

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
            services.AddSingleton<IRTWorkQueuePlatformEventsHandler, RTWorkQueuePlatformEventsHandler>();
            services.AddSingleton<IRTWorkQueueManager, RTWorkQueueManager>();
            services.AddSingleton<IRTWorkQueueTaskSchedulerManager, RTWorkQueueTaskSchedulerManager>();
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddSingleton<IRunner, Runner>();

            var key = typeof(Param).FullName ?? throw new Exception("typeof(Param).FullName is null");
            services.Configure<Param>(hostContext.Configuration.GetSection(key));
        });

        var host = builder.Build();

        return host;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"START");
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"STOP");
        return base.StopAsync(cancellationToken);
    }

    private readonly ILogger _logger;
    private readonly IRunner _runner;


    public VstBridgeWorker(
        ILogger<VstBridgeWorker> logger, 
        IRunner runner, 
        IHostApplicationLifetime hostApplicationLifetime
    )
    {
        _logger = logger;
        _runner = runner;

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

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _runner.StartAsync(stoppingToken);
    }
}

public interface IRunner
{
    Task StartAsync(CancellationToken stoppingToken);
    void Cancel();

    IWindow OpenEditor();
    void CloseEditor();

    void Note(MIDIMessageEvent midiMessage);
    //Task AcceptWebSocket(WebSocket webSocket);
}

public class Runner : IRunner, IDisposable
{
    //private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly IDllManager _dllManager;
    private readonly IWindowManager _windowManager;
    private readonly IRTWorkQueueTaskSchedulerManager _workQueueTaskSchedulerManager;
    private readonly Param _param;

    private bool _disposed;
    private readonly object _sync = new();
    private CancellationTokenSource? _processCancel;
    private Task? _processTask;
    private IEffect<float>? _effect;

    private readonly ElapsedTimeCounter _counter = new();
    private readonly BufferBlock<MIDIMessageEvent2> _midiEventInput = new();

    public Runner(
        ILoggerFactory loggerFactory, 
        IDllManager dllManager, 
        IWindowManager windowManager,
        IRTWorkQueueTaskSchedulerManager workQueueTaskSchedulerManager,
        IOptions<Param> param
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<Runner>();
        _dllManager = dllManager;
        _windowManager = windowManager;
        _workQueueTaskSchedulerManager = workQueueTaskSchedulerManager;

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
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Cancel();
        }
        _disposed = true;
    }

    public async Task StartAsync(CancellationToken stoppingToken)
    {
        lock (_sync)
        {
            if (_processCancel != null)
            {
                _logger.LogInformation("[worker] already started.");
                return;
            }
            _processCancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        }

        var factory = new TaskFactory(_workQueueTaskSchedulerManager.GetTaskScheduler());

        _processTask = factory.StartNew(Run);
        try
        {
            await _processTask.ContinueWith((task) => {

                Cancel();

                _logger.LogInformation(task.Exception, $"[worker] task end");
                _processTask = default;

                _processCancel?.Dispose();
                _processCancel = default;

                _logger.LogInformation("[worker] stopped.");

            }, CancellationToken.None).ConfigureAwait(false);
        }                
        catch (TaskCanceledException)
        {
            _logger.LogInformation("[worker] TaskCanceled");
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "[worker] Exception");
        }
    }

    private async Task Run()
    {
        var processCancel = _processCancel;

        if (processCancel == null)
        {
            throw new InvalidOperationException($"{nameof(processCancel)} is null.");
        }
        if (_param.EffectName == null)
        {
            throw new InvalidOperationException($"{nameof(_param.EffectName)} is null.");
        }

        var ct = processCancel.Token;

        var taskSet = new HashSet<Task>();

        taskSet.Add(_windowManager.StartAsync(ct));

        var blockSize = (int)(_param.SamplingRate * _param.SampleLength);
        var audioInterval = (long)(10_000_000.0 * _param.SampleLength);

        using var buf = new IPCBuffer<float>(_param.EffectName, blockSize * 2 * _param.BufferCount, _loggerFactory);
        //            using var vstBufferPool = new BufferPool<VstBuffer<float>>(param.BufferCount, () => new VstBuffer<float>(blockSize, 2), LoggerFactory);
        using var vstBufferPool = new BufferPool<VstBuffer2<float>>(_param.BufferCount, () => new VstBuffer2<float>(blockSize, 2, buf), _loggerFactory);
        using var pcmPool = new BufferPool<PcmBuffer<float>>(_param.BufferCount, () => new PcmBuffer<float>(blockSize, 2), _loggerFactory);
        using var audioWaiter = new Waiter(_counter, audioInterval, true);
        using var vst = new AudioMaster<float>(_param.SamplingRate, blockSize, _loggerFactory, _counter, _dllManager, _windowManager);
        using var toPcm = new ToPcm<float>(_loggerFactory, _counter);

        _logger.LogInformation($"AddEffect:{_param.EffectName}");

        _effect = vst.AddEffect(_param.EffectName);

        using var wave = new WaveOutFloat(
            0,
            2,
            _param.SamplingRate,
            SPEAKER.FrontLeft | SPEAKER.FrontRight,
            _loggerFactory,
            _counter,
            pcmPool);

        var options = new ExecutionDataflowBlockOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = 1,
            TaskScheduler = _workQueueTaskSchedulerManager.GetTaskScheduler("Pro Audio")
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
                var nowTime = _counter.NowTicks / 10;
                _effect.ProcessEvent(nowTime, _midiEventInput);
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
                processCancel.Cancel();
                _logger.LogError(task.Exception, "Process Exception");
            }
        }
    }

    public void Cancel()
    {
        var processCancel = _processCancel;
        if (processCancel == null)
        {
            _logger.LogInformation("[worker] already stopped.");
            return;
        }

        var task = _processTask;
        try
        {
            processCancel.Cancel();
            task?.Wait();
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "[worker] failed.");
        }
    }

    public IWindow OpenEditor()
    {
        if (_effect == default)
        {
            throw new InvalidOperationException($"{nameof(_effect)} is null.");
        }
        return _effect.OpenEditor();
    }

    public void CloseEditor()
    {
        if (_effect == default)
        {
            throw new InvalidOperationException($"{nameof(_effect)} is null.");
        }
        _effect.CloseEditor();
    }
    public void Note(MIDIMessageEvent midiMessage)
    {
        var m = new MIDIMessageEvent2
        {
            receivedTimeUSec = _counter.NowTicks / 10,
            midiMessageEvent = midiMessage
        };

        _midiEventInput.Post(m);
    }
}
