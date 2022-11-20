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
using Momiji.Core.Window;
using Momiji.Interop.Opus;
using System.Reflection;
using System.Threading.Tasks.Dataflow;

namespace mixerTest;

public class Logic4
{
    private IConfiguration Configuration { get; }
    private ILoggerFactory LoggerFactory { get; }
    private ILogger Logger { get; }
    private IDllManager DllManager { get; }
    private IWindowManager WindowManager { get; }
    private string StreamKey { get; }
    private string IngestHostname { get; }
    private string CaInfoPath { get; }
    private Param Param { get; }

    private CancellationTokenSource ProcessCancel { get; }

    private BufferBlock<MIDIMessageEvent2> MidiEventInput { get; }
    private BufferBlock<MIDIMessageEvent2> MidiEventOutput { get; }

    public Logic4(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        IDllManager dllManager,
        IWindowManager windowManager,
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
        WindowManager = windowManager;
        Param = param;
        ProcessCancel = processCancel;
        MidiEventInput = midiEventInput;
        MidiEventOutput = midiEventOutput;

        StreamKey = Configuration["MIXER_STREAM_KEY"] ?? throw new ArgumentNullException("Configuration[\"MIXER_STREAM_KEY\"]");
        IngestHostname = Configuration["MIXER_INGEST_HOSTNAME"] ?? throw new ArgumentNullException("Configuration[\"MIXER_INGEST_HOSTNAME\"]");

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

    public async Task Run()
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
    private async Task RunLocal()
    {
        var ct = ProcessCancel.Token;
        var taskSet = new HashSet<Task>();

        var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
        Logger.LogInformation($"[loop5] blockSize {blockSize}");

        var audioInterval = (long)(10_000_000.0 * Param.SampleLength);
        Logger.LogInformation($"[loop5] audioInterval {audioInterval}");
        var videoInterval = (long)(10_000_000.0 / Param.MaxFrameRate);
        Logger.LogInformation($"[loop5] videoInterval {videoInterval}");

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
        using var vst = new AudioMaster<float>(Param.SamplingRate, blockSize, LoggerFactory, counter, DllManager, WindowManager);
        using var toPcm = new ToPcm<float>(LoggerFactory, counter);
        using var opus = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, LoggerFactory, counter);
        using var fft = new FFTEncoder(Param.Width, Param.Height, Param.MaxFrameRate, LoggerFactory, counter);
        using var h264 = new H264Encoder(Param.Width, Param.Height, Param.TargetBitrate, Param.MaxFrameRate, LoggerFactory, counter);
        var effect = vst.AddEffect(Param.EffectName);

        var options = new ExecutionDataflowBlockOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = 1,
            MaxMessagesPerTask = DataflowBlockOptions.Unbounded
        };

        {
            var audioBlock =
                new ActionBlock<VstBuffer2<float>>(buffer =>
                {
                    var pcmTask = pcmPool.ReceiveAsync(ct);
                    var audioTask = audioPool.ReceiveAsync(ct);

                    buffer.Log.Clear();
                    audioWaiter.Wait();
                    buffer.Log.Add("[audio] start", counter.NowTicks);

                    //VST
                    var nowTime = counter.NowTicks / 10;
                    effect.ProcessEvent(nowTime, MidiEventInput);
                    effect.ProcessReplacing(nowTime, buffer);

                    //trans
                    var pcm = pcmTask.Result;
                    buffer.Log.Add("[audio] opus input get", counter.NowTicks);
                    toPcm.Execute(buffer, pcm);
                    vstBufferPool.Post(buffer);

                    var audio = audioTask.Result;
                    pcm.Log.Add("[audio] opus output get", counter.NowTicks);
                    opus.Execute(pcm, audio);

                    pcmDrowPool.Post(pcm);

                    //FTL
                    //   ftl.Execute(audio);

                    audioPool.Post(audio);
                }, options);
            taskSet.Add(audioBlock.Completion);
            vstBufferPool.LinkTo(audioBlock);
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

            var intraFrameCount = 0.0;

            var videoBlock =
                new ActionBlock<H264InputBuffer>(buffer =>
                {
                    var videoTask = videoPool.ReceiveAsync(ct);

                    buffer.Log.Clear();

                    videoWaiter.Wait();
                    buffer.Log.Add("[video] start", counter.NowTicks);

                    //FFT
                    fft.Execute(buffer);

                    //H264
                    var video = videoTask.Result;
                    buffer.Log.Add("[video] h264 output get", counter.NowTicks);

                    var insertIntraFrame = (intraFrameCount <= 0);
                    h264.Execute(buffer, video, insertIntraFrame);

                    bmpPool.Post(buffer);
                    if (insertIntraFrame)
                    {
                        intraFrameCount = Param.IntraFrameIntervalUs;
                    }
                    intraFrameCount -= videoInterval;

                    //FTL
                    //ftl.Execute(video);

                    videoPool.Post(video);
                }, options);
            taskSet.Add(videoBlock.Completion);
            bmpPool.LinkTo(videoBlock);
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

    private async Task RunConnect()
    {
        var ct = ProcessCancel.Token;
        var taskSet = new HashSet<Task>();

        var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
        Logger.LogInformation($"[loop4] blockSize {blockSize}");

        var audioInterval = (long)(10_000_000.0 * Param.SampleLength);
        Logger.LogInformation($"[loop4] audioInterval {audioInterval}");
        var videoInterval = (long)(10_000_000.0 / Param.MaxFrameRate);
        Logger.LogInformation($"[loop4] videoInterval {videoInterval}");

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
        using var vst = new AudioMaster<float>(Param.SamplingRate, blockSize, LoggerFactory, counter, DllManager, WindowManager);
        using var toPcm = new ToPcm<float>(LoggerFactory, counter);
        using var opus = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, LoggerFactory, counter);
        using var fft = new FFTEncoder(Param.Width, Param.Height, Param.MaxFrameRate, LoggerFactory, counter);
        using var h264 = new H264Encoder(Param.Width, Param.Height, Param.TargetBitrate, Param.MaxFrameRate, LoggerFactory, counter);
        var effect = vst.AddEffect(Param.EffectName);

        using var ftl = new FtlIngest(StreamKey, IngestHostname, LoggerFactory, counter, audioInterval, videoInterval, default, CaInfoPath);
        ftl.Connect();

        var options = new ExecutionDataflowBlockOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = 1,
            MaxMessagesPerTask = DataflowBlockOptions.Unbounded
        };

        {
            var audioBlock =
                new ActionBlock<VstBuffer2<float>>(buffer =>
                {
                    var pcmTask = pcmPool.ReceiveAsync(ct);
                    var audioTask = audioPool.ReceiveAsync(ct);

                    buffer.Log.Clear();
                    audioWaiter.Wait();
                    buffer.Log.Add("[audio] start", counter.NowTicks);

                    //VST
                    var nowTime = counter.NowTicks / 10;
                    effect.ProcessEvent(nowTime, MidiEventInput);
                    effect.ProcessReplacing(nowTime, buffer);

                    //trans
                    var pcm = pcmTask.Result;
                    toPcm.Execute(buffer, pcm);
                    vstBufferPool.Post(buffer);

                    var audio = audioTask.Result;
                    pcm.Log.Add("[audio] opus output get", counter.NowTicks);
                    opus.Execute(pcm, audio);

                    pcmDrowPool.Post(pcm);

                    //FTL
                    ftl.Execute(audio);
                    audioPool.Post(audio);
                }, options);
            taskSet.Add(audioBlock.Completion);
            vstBufferPool.LinkTo(audioBlock);
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

            var intraFrameCount = 0.0;

            var videoBlock =
                new ActionBlock<H264InputBuffer>(buffer =>
                {
                    var videoTask = videoPool.ReceiveAsync(ct);

                    buffer.Log.Clear();

                    videoWaiter.Wait();
                    buffer.Log.Add("[video] start", counter.NowTicks);

                    //FFT
                    fft.Execute(buffer);

                    //H264
                    var video = videoTask.Result;
                    buffer.Log.Add("[video] h264 output get", counter.NowTicks);
                    var insertIntraFrame = (intraFrameCount <= 0);
                    h264.Execute(buffer, video, insertIntraFrame);
                    bmpPool.Post(buffer);
                    if (insertIntraFrame)
                    {
                        intraFrameCount = Param.IntraFrameIntervalUs;
                    }
                    intraFrameCount -= videoInterval;

                    //FTL
                    ftl.Execute(video);
                    videoPool.Post(video);
                }, options);
            taskSet.Add(videoBlock.Completion);
            bmpPool.LinkTo(videoBlock);
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
}
