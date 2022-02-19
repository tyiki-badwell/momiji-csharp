using Momiji.Core.Buffer;
using Momiji.Core.Configuration;
using Momiji.Core.Dll;
using Momiji.Core.FFT;
using Momiji.Core.Ftl;
using Momiji.Core.H264;
using Momiji.Core.Opus;
using Momiji.Core.SharedMemory;
using Momiji.Core.Timer;
using Momiji.Core.Trans;
using Momiji.Core.Vst;
using Momiji.Core.Wave;
using Momiji.Core.WebMidi;
using Momiji.Interop.Opus;
using System.Reflection;
using System.Threading.Tasks.Dataflow;

namespace mixerTest
{
    public class Logic2 : ILogic
    {
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private IDllManager DllManager { get; }
        private string StreamKey { get; }
        private string IngestHostname { get; }
        private string CaInfoPath { get; }
        private Param Param { get; }

        private CancellationTokenSource ProcessCancel { get; }

        private BufferBlock<MIDIMessageEvent2> MidiEventInput { get; }
        private BufferBlock<MIDIMessageEvent2> MidiEventOutput { get; }

        private IEffect<float>? effect;

        public Logic2(
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            IDllManager dllManager,
            Param param,
            BufferBlock<MIDIMessageEvent2> midiEventInput,
            BufferBlock<MIDIMessageEvent2> midiEventOutput,
            CancellationTokenSource processCancel
        )
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Runner>();
            DllManager = dllManager;
            Param = param;
            ProcessCancel = processCancel;
            MidiEventInput = midiEventInput;
            MidiEventOutput = midiEventOutput;

            StreamKey = Configuration["MIXER_STREAM_KEY"];
            IngestHostname = Configuration["MIXER_INGEST_HOSTNAME"];

            var assembly = Assembly.GetExecutingAssembly();
            var directoryName = Path.GetDirectoryName(assembly.Location);
            if (directoryName == default)
            {
                throw new InvalidOperationException($"GetDirectoryName({assembly.Location}) failed.");
            }

            CaInfoPath =
                Path.Combine(
                    directoryName,
                    "lib",
                    "cacert.pem"
                );
        }

        public async Task RunAsync()
        {
            if (Param.Local)
            {
                await RunLocal().ConfigureAwait(false);
            }
            else
            {
                await RunConnect().ConfigureAwait(false);
            }
        }

        private async Task RunConnect()
        {
            var ct = ProcessCancel.Token;
            var taskSet = new HashSet<Task>();

            var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
            Logger.LogInformation($"[loop3] blockSize {blockSize}");

            var audioInterval = (long)(10_000_000.0 * Param.SampleLength);
            Logger.LogInformation($"[loop3] audioInterval {audioInterval}");
            var videoInterval = (long)(10_000_000.0 / Param.MaxFrameRate);
            Logger.LogInformation($"[loop3] videoInterval {videoInterval}");

            var counter = new ElapsedTimeCounter();
            using var audioWaiter = new Waiter(counter, audioInterval);
            using var videoWaiter = new Waiter(counter, videoInterval);
            using var buf = new IPCBuffer<float>(Param.EffectName, blockSize * 2 * Param.BufferCount, LoggerFactory);
            using var vstBufferPool = new BufferPool<VstBuffer2<float>>(Param.BufferCount, () => new VstBuffer2<float>(blockSize, 2, buf), LoggerFactory);
            using var pcmPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => new PcmBuffer<float>(blockSize, 2), LoggerFactory);
            using var audioPool = new BufferPool<OpusOutputBuffer>(Param.BufferCount, () => new OpusOutputBuffer(5000), LoggerFactory);
            using var pcmDrowPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => new PcmBuffer<float>(blockSize, 2), LoggerFactory);
            using var bmpPool = new BufferPool<H264InputBuffer>(Param.BufferCount, () => new H264InputBuffer(Param.Width, Param.Height), LoggerFactory);
            using var videoPool = new BufferPool<H264OutputBuffer>(Param.BufferCount, () => new H264OutputBuffer(200000), LoggerFactory);
            using var vst = new AudioMaster<float>(Param.SamplingRate, blockSize, LoggerFactory, counter, DllManager);
            using var toPcm = new ToPcm<float>(LoggerFactory, counter);
            using var opus = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, LoggerFactory, counter);
            using var fft = new FFTEncoder(Param.Width, Param.Height, Param.MaxFrameRate, LoggerFactory, counter);
            using var h264 = new H264Encoder(Param.Width, Param.Height, Param.TargetBitrate, Param.MaxFrameRate, LoggerFactory, counter);
            
            effect = vst.AddEffect(Param.EffectName);
            //effect.OpenEditor();

            using var ftl = new FtlIngest(StreamKey, IngestHostname, LoggerFactory, counter, audioInterval, videoInterval, default, CaInfoPath);
            ftl.Connect();

            var options = new ExecutionDataflowBlockOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = 1
            };

            {
                var audioStartBlock =
                    new TransformBlock<VstBuffer2<float>, VstBuffer2<float>>(buffer => {
                        buffer.Log.Clear();
                        audioWaiter.Wait();
                        return buffer;
                    }, options);
                taskSet.Add(audioStartBlock.Completion);
                vstBufferPool.LinkTo(audioStartBlock);

                var vstBlock =
                    new TransformBlock<VstBuffer2<float>, PcmBuffer<float>>(async buffer =>
                    {
                        var pcmTask = pcmPool.ReceiveAsync(ct);

                        buffer.Log.Add("[audio] start", counter.NowTicks);

                        //VST
                        var nowTime = counter.NowTicks / 10;
                        effect.ProcessEvent(nowTime, MidiEventInput);
                        effect.ProcessReplacing(nowTime, buffer);

                        //trans
                        var pcm = await pcmTask.ConfigureAwait(false);
                        toPcm.Execute(buffer, pcm);
                        vstBufferPool.Post(buffer);

                        return pcm;
                    }, options);
                taskSet.Add(vstBlock.Completion);
                audioStartBlock.LinkTo(vstBlock);

                var opusBlock =
                    new TransformBlock<PcmBuffer<float>, OpusOutputBuffer>(buffer =>
                    {
                        buffer.Log.Add("[audio] opus input get", counter.NowTicks);
                        var audio = audioPool.Receive(ct);
                        buffer.Log.Add("[audio] ftl output get", counter.NowTicks);
                        opus.Execute(buffer, audio);

                        pcmDrowPool.Post(buffer);

                        return audio;
                    }, options);
                taskSet.Add(opusBlock.Completion);
                vstBlock.LinkTo(opusBlock);

                var ftlBlock =
                    new ActionBlock<OpusOutputBuffer>(buffer =>
                    {
                        //FTL
                        buffer.Log.Add("[audio] ftl input get", counter.NowTicks);
                        ftl.Execute(buffer);
                        audioPool.Post(buffer);
                    }, options);
                taskSet.Add(ftlBlock.Completion);
                opusBlock.LinkTo(ftlBlock);
            }

            {
                var midiDataStoreBlock =
                    new ActionBlock<MIDIMessageEvent2>(buffer =>
                    {
                        fft.Receive(buffer);
                    }, options);
                taskSet.Add(midiDataStoreBlock.Completion);
                MidiEventOutput.LinkTo(midiDataStoreBlock);

                var pcmDataStoreBlock =
                    new ActionBlock<PcmBuffer<float>>(buffer =>
                    {
                        fft.Receive(buffer);
                        pcmPool.Post(buffer);
                    }, options);
                taskSet.Add(pcmDataStoreBlock.Completion);
                pcmDrowPool.LinkTo(pcmDataStoreBlock);

                var videoStartBlock =
                    new TransformBlock<H264InputBuffer, H264InputBuffer>(buffer => {
                        buffer.Log.Clear();
                        videoWaiter.Wait();
                        return buffer;
                    }, options);
                taskSet.Add(videoStartBlock.Completion);
                bmpPool.LinkTo(videoStartBlock);

                var fftBlock =
                    new TransformBlock<H264InputBuffer, H264InputBuffer>(buffer =>
                    {
                        buffer.Log.Add("[video] start", counter.NowTicks);

                        //FFT
                        fft.Execute(buffer);

                        return buffer;
                    }, options);
                taskSet.Add(fftBlock.Completion);
                videoStartBlock.LinkTo(fftBlock);

                var intraFrameCount = 0.0;
                var h264Block =
                    new TransformBlock<H264InputBuffer, H264OutputBuffer>(buffer =>
                    {
                        //H264
                        buffer.Log.Add("[video] h264 input get", counter.NowTicks);
                        var video = videoPool.Receive(ct);
                        buffer.Log.Add("[video] ftl output get", counter.NowTicks);
                        var insertIntraFrame = (intraFrameCount <= 0);
                        h264.Execute(buffer, video, insertIntraFrame);
                        bmpPool.Post(buffer);
                        if (insertIntraFrame)
                        {
                            intraFrameCount = Param.IntraFrameIntervalUs;
                        }
                        intraFrameCount -= videoInterval;
                        return video;
                    }, options);
                taskSet.Add(h264Block.Completion);
                fftBlock.LinkTo(h264Block);

                var ftlBlock =
                    new ActionBlock<H264OutputBuffer>(buffer =>
                    {
                        //FTL
                        buffer.Log.Add("[video] ftl input get", counter.NowTicks);
                        ftl.Execute(buffer);
                        videoPool.Post(buffer);
                    }, options);
                taskSet.Add(ftlBlock.Completion);
                h264Block.LinkTo(ftlBlock);
            }

            while (taskSet.Count > 0)
            {
                var task = await Task.WhenAny(taskSet).ConfigureAwait(false);
                taskSet.Remove(task);
                if (task.IsFaulted)
                {
                    ProcessCancel.Cancel();
                    Logger.LogError(task.Exception, "Process Exception");
                }
            }
        }

        private async Task RunLocal()
        {
            var ct = ProcessCancel.Token;
            var taskSet = new HashSet<Task>();

            var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
            var audioInterval = (long)(10_000_000.0 * Param.SampleLength);

            using var buf = new IPCBuffer<float>(Param.EffectName, blockSize * 2 * Param.BufferCount, LoggerFactory);
            using var vstBufferPool = new BufferPool<VstBuffer2<float>>(Param.BufferCount, () => new VstBuffer2<float>(blockSize, 2, buf), LoggerFactory);
            using var pcmPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => new PcmBuffer<float>(blockSize, 2), LoggerFactory);
            var counter = new ElapsedTimeCounter();
            using var audioWaiter = new Waiter(counter, audioInterval);
            using var vst = new AudioMaster<float>(Param.SamplingRate, blockSize, LoggerFactory, counter, DllManager);
            using var toPcm = new ToPcm<float>(LoggerFactory, counter);
            
            effect = vst.AddEffect(Param.EffectName);
            //effect.OpenEditor();

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
                new TransformBlock<VstBuffer2<float>, PcmBuffer<float>>(async buffer =>
                {
                    var pcmTask = pcmPool.ReceiveAsync(ct);

                    //VST
                    var nowTime = counter.NowTicks / 10;
                    effect.ProcessEvent(nowTime, MidiEventInput);
                    effect.ProcessReplacing(nowTime, buffer);

                    //trans
                    var pcm = await pcmTask.ConfigureAwait(false);
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
                    ProcessCancel.Cancel();
                    Logger.LogError(task.Exception, "Process Exception");
                }
            }
        }

        public void OpenEditor()
        {
            effect?.OpenEditor(ProcessCancel.Token);
        }

        public void CloseEditor()
        {
            effect?.CloseEditorAsync().Wait();
        }
    }
}