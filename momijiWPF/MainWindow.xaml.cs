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
using System.Windows.Interop;

namespace momijiWPF
{
    public class Param
    {
        public int BufferCount { get; set; }
        public bool Local { get; set; }
        public bool Connect { get; set; }

        public int Width { get; set; }
        public int Height { get; set; }
        public int TargetBitrate { get; set; }
        public float MaxFrameRate { get; set; }
        public int IntraFrameIntervalUs { get; set; }

        public string EffectName { get; set; }
        public int SamplingRate { get; set; }
        public float SampleLength { get; set; }
        /*
         この式を満たさないとダメ
         new_size = blockSize
         Fs = samplingRate

          if (frame_size<Fs/400)
            return -1;
          if (400*new_size!=Fs   && 200*new_size!=Fs   && 100*new_size!=Fs   &&
              50*new_size!=Fs   &&  25*new_size!=Fs   &&  50*new_size!=3*Fs &&
              50*new_size!=4*Fs &&  50*new_size!=5*Fs &&  50*new_size!=6*Fs)
            return -1;

        0.0025
        0.005
        0.01
        0.02
        0.04
        0.06
        0.08
        0.1
        0.12
         */
    }

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

            var param = new Param();
            configuration.GetSection("Param").Bind(param);

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConfiguration(configuration);
                builder.AddConsole();
                builder.AddDebug();
            });
            var logger = loggerFactory.CreateLogger<MainWindow>();

            using var processCancel = new CancellationTokenSource();

            var ct = processCancel.Token;
            var taskSet = new HashSet<Task>();

            using var dllManager = new DllManager(configuration, loggerFactory);

            var blockSize = (int)(param.SamplingRate * param.SampleLength);
            var audioInterval = 1_000_000.0 * param.SampleLength;

            using var vstBufferPool = new BufferPool<VstBuffer<float>>(param.BufferCount, () => new VstBuffer<float>(blockSize, 2), loggerFactory);
            using var pcmPool = new BufferPool<PcmBuffer<float>>(param.BufferCount, () => new PcmBuffer<float>(blockSize, 2), loggerFactory);
            using var timer = new Momiji.Core.Timer();
            using var audioWaiter = new Waiter(timer, audioInterval);
            using var vst = new AudioMaster<float>(param.SamplingRate, blockSize, loggerFactory, timer, dllManager);
            using var toPcm = new ToPcm<float>(loggerFactory, timer);
            var effect = vst.AddEffect(param.EffectName);

            using var wave = new WaveOutFloat(
                0,
                2,
                param.SamplingRate,
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

