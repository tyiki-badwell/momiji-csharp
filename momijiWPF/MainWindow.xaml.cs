using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core;
using Momiji.Core.Trans;
using Momiji.Core.Vst;
using Momiji.Core.Wave;
using Momiji.Interop.Wave;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows;

namespace momijiWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        public async Task Run()
        {
            var configuration =
                new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddFilter("Momiji", LogLevel.Information);
                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
                builder.AddConsole();
                builder.AddDebug();
            });
            var logger = loggerFactory.CreateLogger<MainWindow>();

            using var processCancel = new CancellationTokenSource();

            int bufferCount = 2;

            string effectName = "Dexed.dll";
            int samplingRate = 48000;
            float sampleLength = 0.06f;

            var ct = processCancel.Token;
            var taskSet = new HashSet<Task>();

            using var dllManager = new DllManager(configuration, loggerFactory);

            var blockSize = (int)(samplingRate * sampleLength);
            var audioInterval = 1_000_000.0 * sampleLength;

            using var vstBufferPool = new BufferPool<VstBuffer<float>>(bufferCount, () => new VstBuffer<float>(blockSize, 2), loggerFactory);
            using var pcmPool = new BufferPool<PcmBuffer<float>>(bufferCount, () => new PcmBuffer<float>(blockSize, 2), loggerFactory);
            using var timer = new Momiji.Core.Timer();
            using var audioWaiter = new Waiter(timer, audioInterval);
            using var vst = new AudioMaster<float>(samplingRate, blockSize, loggerFactory, timer, dllManager);
            using var toPcm = new ToPcm<float>(loggerFactory, timer);
            var effect = vst.AddEffect(effectName);

            using var wave = new WaveOutFloat(
                0,
                2,
                samplingRate,
                SPEAKER.FrontLeft | SPEAKER.FrontRight,
                loggerFactory,
                timer,
                pcmPool);

            var options = new ExecutionDataflowBlockOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = 1
            };

            var vstBlock =
                new TransformBlock<VstBuffer<float>, PcmBuffer<float>>(async buffer =>
                {
                    buffer.Log.Clear();
                    await audioWaiter.Wait(ct).ConfigureAwait(false);
                    //VST
                    var nowTime = timer.USecDouble;
                    //    effect.ProcessEvent(nowTime, MidiEventInput);
                    effect.ProcessReplacing(nowTime, buffer);

                    //trans
                    var pcm = pcmPool.Receive();
                    toPcm.Execute(buffer, pcm);
                    vstBufferPool.Post(buffer);

                    return pcm;
                }, options);
            taskSet.Add(vstBlock.Completion);
            vstBufferPool.LinkTo(vstBlock);

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
                    logger.LogError(task.Exception, "Process Exception");
                }
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            await Run().ConfigureAwait(false);
        }
    }

}

