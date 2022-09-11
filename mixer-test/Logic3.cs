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
using System.Runtime.InteropServices;
using System.Threading.Tasks.Dataflow;
using Windows.Media.Audio;
using Windows.Media.MediaProperties;
using WinRT;

namespace mixerTest;

public class Logic3
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

    public Logic3(
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

    public async Task Run()
    {
        if (Param.Local)
        {
            await RunLocal().ConfigureAwait(false);
        }
        else
        {
            //await RunConnect().ConfigureAwait(false);
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
            MaxDegreeOfParallelism = 1
        };

        {
            var vstBlock =
                new TransformBlock<VstBuffer2<float>, PcmBuffer<float>>(async buffer =>
                {
                    var pcmTask = pcmPool.ReceiveAsync(ct);

                    buffer.Log.Clear();
                    audioWaiter.Wait();

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
            vstBufferPool.LinkTo(vstBlock);

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

            var fftBlock =
                new TransformBlock<H264InputBuffer, H264InputBuffer>(buffer =>
                {
                    buffer.Log.Clear();

                    videoWaiter.Wait();
                    buffer.Log.Add("[video] start", counter.NowTicks);

                    //FFT
                    fft.Execute(buffer);

                    return buffer;
                }, options);
            taskSet.Add(fftBlock.Completion);
            bmpPool.LinkTo(fftBlock);

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

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    private double audioWaveTheta;

    private async Task RunLocal()
    {
        var ct = ProcessCancel.Token;
        var taskSet = new HashSet<Task>();

        var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Media.Devices.MediaDevice.GetAudioRenderSelector());


        AudioGraph audioGraph;
        {
            var settings = new AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.Media)
            {
                QuantumSizeSelectionMode = QuantumSizeSelectionMode.LowestLatency,
                DesiredSamplesPerQuantum = 1000,
                AudioRenderCategory = Windows.Media.Render.AudioRenderCategory.GameMedia,
                MaxPlaybackSpeedFactor = 1,
                DesiredRenderDeviceAudioProcessing = Windows.Media.AudioProcessing.Raw
            };

            var result = await AudioGraph.CreateAsync(settings);
            if (result.Status != AudioGraphCreationStatus.Success)
            {
                throw new InvalidOperationException("create failed.", result.ExtendedError);
            }
            audioGraph = result.Graph;
        }
        Logger.LogInformation($"[loop3] audioGraph.SamplesPerQuantum {audioGraph.SamplesPerQuantum}");
        Logger.LogInformation($"[loop3] audioGraph.LatencyInSamples {audioGraph.LatencyInSamples}");

        AudioDeviceOutputNode outNode;
        {
            var result = await audioGraph.CreateDeviceOutputNodeAsync();
            if (result.Status != AudioDeviceNodeCreationStatus.Success)
            {
                throw new InvalidOperationException("create failed.", result.ExtendedError);
            }
            outNode = result.DeviceOutputNode;
        }

        AudioFrameInputNode inNode;
        {
            var prop = AudioEncodingProperties.CreatePcm((uint)Param.SamplingRate, 2, sizeof(float) * 8);
            inNode = audioGraph.CreateFrameInputNode(prop);
            inNode.Stop();
            inNode.QuantumStarted += (AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args) =>
            {
                var samples = (uint)args.RequiredSamples;

                var bufferSize = samples * sizeof(float);
                var frame = new Windows.Media.AudioFrame(bufferSize);

                using (var buffer = frame.LockBuffer(Windows.Media.AudioBufferAccessMode.Write))
                using (var reference = buffer.CreateReference())
                {
                    unsafe
                    {
                        reference.As<IMemoryBufferByteAccess>().GetBuffer(out byte* dataInBytes, out uint capacityInBytes);
                        var dataInFloat = (float*)dataInBytes;
                        float freq = 0.480f; // choosing to generate frequency of 1kHz
                        float amplitude = 0.3f;
                        int sampleRate = (int)audioGraph.EncodingProperties.SampleRate;
                        double sampleIncrement = (freq * (Math.PI * 2)) / sampleRate;

                        // Generate a 1kHz sine wave and populate the values in the memory buffer
                        for (int i = 0; i < samples; i++)
                        {
                            double sinValue = amplitude * Math.Sin(audioWaveTheta);
                            dataInFloat[i] = (float)sinValue;
                            audioWaveTheta += sampleIncrement;
                        }
                    }
                }
                sender.AddFrame(frame);
            };

            inNode.AddOutgoingConnection(outNode);
            inNode.Start();
        }

        //audioGraph.Start();

        var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
        var audioInterval = (long)(10_000_000.0 * Param.SampleLength);

        using var buf = new IPCBuffer<float>(Param.EffectName, blockSize * 2 * Param.BufferCount, LoggerFactory);
        using var vstBufferPool = new BufferPool<VstBuffer2<float>>(Param.BufferCount, () => new VstBuffer2<float>(blockSize, 2, buf), LoggerFactory);
        using var pcmPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => new PcmBuffer<float>(blockSize, 2), LoggerFactory);
        var counter = new ElapsedTimeCounter();
        using var audioWaiter = new Waiter(counter, audioInterval);
        using var vst = new AudioMaster<float>(Param.SamplingRate, blockSize, LoggerFactory, counter, DllManager, WindowManager);
        using var toPcm = new ToPcm<float>(LoggerFactory, counter);
        var effect = vst.AddEffect(Param.EffectName);

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

        var vstBlock =
            new TransformBlock<VstBuffer2<float>, PcmBuffer<float>>(async buffer =>
            {
                var pcmTask = pcmPool.ReceiveAsync(ct);

                buffer.Log.Clear();
                audioWaiter.Wait();
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
                ProcessCancel.Cancel();
                Logger.LogError(task.Exception, "Process Exception");
            }
        }
    }
}
