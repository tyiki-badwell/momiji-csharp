using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Core.Configuration;
using Momiji.Core.Dll;
using Momiji.Core.SharedMemory;
using Momiji.Core.Timer;
using Momiji.Core.Trans;
using Momiji.Core.Wave;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.Vst.Worker
{
    public class VstBridgeWorker : BackgroundService
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<VstBridgeWorker>();
                    services.AddSingleton<IDllManager, DllManager>();
                    services.AddSingleton<IRunner, Runner>();
                });

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            Logger.LogInformation($"START");
            await base.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            Logger.LogInformation($"STOP");
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        private ILogger Logger { get; }
        private IRunner Runner { get; }

        public VstBridgeWorker(ILogger<VstBridgeWorker> logger, IRunner runner)
        {
            Logger = logger;
            Runner = runner;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Runner.StartAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    public interface IRunner
    {
        Task StartAsync(CancellationToken stoppingToken);
        void Cancel();

        void OpenEditor();
        Task CloseEditorAsync();

        //void Note(MIDIMessageEvent[] midiMessage);
        //Task AcceptWebSocket(WebSocket webSocket);
    }

    public class Runner : IRunner, IDisposable
    {
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private IDllManager DllManager { get; }
        private Param Param { get; set; }

        private bool disposed;
        private readonly object sync = new();
        private CancellationTokenSource processCancel;
        private Task processTask;
        private IEffect<float> effect;

        public Runner(IConfiguration configuration, ILoggerFactory loggerFactory, IDllManager dllManager)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Runner>();
            DllManager = dllManager;

            var section = Configuration.GetSection(typeof(Param).FullName);
            var param = section.Get<Param>();

            if (param == default)
            {
                throw new ArgumentNullException(typeof(Param).FullName);
            }

            Logger.LogInformation($"BufferCount:{param.BufferCount}");
            Logger.LogInformation($"Local:{param.Local}");
            Logger.LogInformation($"Connect:{param.Connect}");
            Logger.LogInformation($"Width:{param.Width}");
            Logger.LogInformation($"Height:{param.Height}");
            Logger.LogInformation($"TargetBitrate:{param.TargetBitrate}");
            Logger.LogInformation($"MaxFrameRate:{param.MaxFrameRate}");
            Logger.LogInformation($"IntraFrameIntervalUs:{param.IntraFrameIntervalUs}");
            Logger.LogInformation($"EffectName:{param.EffectName}");
            Logger.LogInformation($"SamplingRate:{param.SamplingRate}");
            Logger.LogInformation($"SampleLength:{param.SampleLength}");

            Param = param;
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
                Cancel();
            }
            disposed = true;
        }

        public async Task StartAsync(CancellationToken stoppingToken)
        {
            if (processCancel != null)
            {
                Logger.LogInformation("[worker] already started.");
                return;
            }
            processCancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            processTask = Run();
            await processTask.ConfigureAwait(false);
        }

        private async Task Run()
        {
            var ct = processCancel.Token;

            var taskSet = new HashSet<Task>();

            var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
            var audioInterval = (long)(10_000_000.0 * Param.SampleLength);

            using var buf = new IPCBuffer<float>(Param.EffectName, blockSize * 2 * Param.BufferCount, LoggerFactory);
            //            using var vstBufferPool = new BufferPool<VstBuffer<float>>(param.BufferCount, () => new VstBuffer<float>(blockSize, 2), LoggerFactory);
            using var vstBufferPool = new BufferPool<VstBuffer2<float>>(Param.BufferCount, () => new VstBuffer2<float>(blockSize, 2, buf), LoggerFactory);
            using var pcmPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => new PcmBuffer<float>(blockSize, 2), LoggerFactory);
            var counter = new ElapsedTimeCounter();
            using var audioWaiter = new Waiter(counter, audioInterval);
            using var vst = new AudioMaster<float>(Param.SamplingRate, blockSize, LoggerFactory, counter, DllManager);
            using var toPcm = new ToPcm<float>(LoggerFactory, counter);

            Logger.LogInformation($"AddEffect:{Param.EffectName}");

            effect = vst.AddEffect(Param.EffectName);

            using var wave = new WaveOutFloat(
                0,
                2,
                Param.SamplingRate,
                SPEAKER.FrontLeft | SPEAKER.FrontRight,
                LoggerFactory,
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
                    audioWaiter.Wait();
                    return buffer;
                }, options);
            taskSet.Add(audioStartBlock.Completion);
            vstBufferPool.LinkTo(audioStartBlock);

            var vstBlock =
                new TransformBlock<VstBuffer2<float>, PcmBuffer<float>>(buffer =>
                {
                    //VST
                    var nowTime = counter.NowTicks / 10;
                    effect.ProcessReplacing(nowTime, buffer);

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
                    Logger.LogError(task.Exception, "Process Exception");
                }
            }
        }

        public void Cancel()
        {
            lock(sync)
            {
                if (processCancel == null)
                {
                    Logger.LogInformation("[worker] already stopped.");
                    return;
                }

                try
                {
                    processCancel.Cancel();
                    processTask.Wait();
                }
                catch (AggregateException e)
                {
                    Logger.LogInformation(e, "[worker] Process Cancel Exception");
                }
                finally
                {
                    processCancel.Dispose();
                    processCancel = null;

                    processTask?.Dispose();
                    processTask = null;
                }
                Logger.LogInformation("[worker] stopped.");
            }
        }

        public void OpenEditor()
        {
            effect.OpenEditor(processCancel.Token);
        }

        public async Task CloseEditorAsync()
        {
            await effect.CloseEditorAsync().ConfigureAwait(false);
        }
    }
}
