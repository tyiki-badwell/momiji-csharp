using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core;
using Momiji.Core.FFT;
using Momiji.Core.Ftl;
using Momiji.Core.H264;
using Momiji.Core.Opus;
using Momiji.Core.Trans;
using Momiji.Core.Vst;
using Momiji.Core.Wave;
using Momiji.Core.WebMidi;
using Momiji.Interop.Opus;
using Momiji.Interop.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace mixerTest
{
    public class Logic2
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

            CaInfoPath =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "lib",
                    "cacert.pem"
                );
        }

        public async Task Run()
        {
            if (Param.Local)
            {
                await Loop2().ConfigureAwait(false);
            }
            else
            {
                await Loop3().ConfigureAwait(false);
            }
        }

        private async Task Loop3()
        {
            var ct = ProcessCancel.Token;
            var taskSet = new HashSet<Task>();

            var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
            Logger.LogInformation($"[loop3] blockSize {blockSize}");

            var audioInterval = 1_000_000.0 * Param.SampleLength;
            Logger.LogInformation($"[loop3] audioInterval {audioInterval}");
            var videoInterval = 1_000_000.0 / Param.MaxFrameRate;
            Logger.LogInformation($"[loop3] videoInterval {videoInterval}");

            using var timer = new Momiji.Core.Timer();
            using var audioWaiter = new Waiter(timer, audioInterval, ct);
            using var videoWaiter = new Waiter(timer, videoInterval, ct);
            using var vstBufferPool = new BufferPool<VstBuffer<float>>(Param.BufferCount, () => new VstBuffer<float>(blockSize, 2), LoggerFactory);
            using var pcmPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => new PcmBuffer<float>(blockSize, 2), LoggerFactory);
            using var audioPool = new BufferPool<OpusOutputBuffer>(Param.BufferCount, () => new OpusOutputBuffer(5000), LoggerFactory);
            using var pcmDrowPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => new PcmBuffer<float>(blockSize, 2), LoggerFactory);
            using var bmpPool = new BufferPool<H264InputBuffer>(Param.BufferCount, () => new H264InputBuffer(Param.Width, Param.Height), LoggerFactory);
            using var videoPool = new BufferPool<H264OutputBuffer>(Param.BufferCount, () => new H264OutputBuffer(200000), LoggerFactory);
            using var vst = new AudioMaster<float>(Param.SamplingRate, blockSize, LoggerFactory, timer, DllManager);
            using var toPcm = new ToPcm<float>(LoggerFactory, timer);
            using var opus = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, LoggerFactory, timer);
            using var fft = new FFTEncoder(Param.Width, Param.Height, Param.MaxFrameRate, LoggerFactory, timer);
            using var h264 = new H264Encoder(Param.Width, Param.Height, Param.TargetBitrate, Param.MaxFrameRate, LoggerFactory, timer);
            var effect = vst.AddEffect(Param.EffectName);

            using var ftl = new FtlIngest(StreamKey, IngestHostname, LoggerFactory, timer, audioInterval, videoInterval, Param.Connect, default, CaInfoPath);
            ftl.Connect();

            var options = new ExecutionDataflowBlockOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = 1
            };

            {
                var vstBlock =
                    new TransformBlock<VstBuffer<float>, PcmBuffer<float>>(buffer =>
                    {
                        var pcmTask = pcmPool.ReceiveAsync(ct);

                        buffer.Log.Clear();
                        audioWaiter.Wait();
                        buffer.Log.Add("[audio] start", timer.USecDouble);

                        //VST
                        var nowTime = timer.USecDouble;
                        effect.ProcessEvent(nowTime, MidiEventInput);
                        effect.ProcessReplacing(nowTime, buffer);

                        //trans
                        var pcm = pcmTask.Result;
                        toPcm.Execute(buffer, pcm);
                        vstBufferPool.SendAsync(buffer);

                        return pcm;
                    }, options);
                taskSet.Add(vstBlock.Completion);
                vstBufferPool.LinkTo(vstBlock);

                var opusBlock =
                    new TransformBlock<PcmBuffer<float>, OpusOutputBuffer>(buffer =>
                    {
                        buffer.Log.Add("[audio] opus input get", timer.USecDouble);
                        var audio = audioPool.Receive(ct);
                        buffer.Log.Add("[audio] ftl output get", timer.USecDouble);
                        opus.Execute(buffer, audio);

                        pcmDrowPool.SendAsync(buffer);

                        return audio;
                    }, options);
                taskSet.Add(opusBlock.Completion);
                vstBlock.LinkTo(opusBlock);

                var ftlBlock =
                    new ActionBlock<OpusOutputBuffer>(buffer =>
                    {
                        //FTL
                        buffer.Log.Add("[audio] ftl input get", timer.USecDouble);
                        ftl.Execute(buffer);
                        audioPool.SendAsync(buffer);
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
                        pcmPool.SendAsync(buffer);
                    }, options);
                taskSet.Add(pcmDataStoreBlock.Completion);
                pcmDrowPool.LinkTo(pcmDataStoreBlock);

                var fftBlock =
                    new TransformBlock<H264InputBuffer, H264InputBuffer>(buffer =>
                    {
                        buffer.Log.Clear();

                        videoWaiter.Wait();
                        buffer.Log.Add("[video] start", timer.USecDouble);

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
                        buffer.Log.Add("[video] h264 input get", timer.USecDouble);
                        var video = videoPool.Receive(ct);
                        buffer.Log.Add("[video] ftl output get", timer.USecDouble);
                        var insertIntraFrame = (intraFrameCount <= 0);
                        h264.Execute(buffer, video, insertIntraFrame);
                        bmpPool.SendAsync(buffer);
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
                        buffer.Log.Add("[video] ftl input get", timer.USecDouble);
                        ftl.Execute(buffer);
                        videoPool.SendAsync(buffer);
                    }, options);
                taskSet.Add(ftlBlock.Completion);
                h264Block.LinkTo(ftlBlock);
            }

            while (taskSet.Count > 0)
            {
                var any = Task.WhenAny(taskSet);
                await any.ConfigureAwait(false);
                any.Wait();
                var task = any.Result;
                taskSet.Remove(task);
                if (task.IsFaulted)
                {
                    ProcessCancel.Cancel();
                    Logger.LogError(task.Exception, "Process Exception");
                }
            }
        }

        private async Task Loop2()
        {
            var ct = ProcessCancel.Token;
            var taskSet = new HashSet<Task>();

            var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
            var audioInterval = 1_000_000.0 * Param.SampleLength;

            using var vstBufferPool = new BufferPool<VstBuffer<float>>(Param.BufferCount, () => new VstBuffer<float>(blockSize, 2), LoggerFactory);
            using var pcmPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => new PcmBuffer<float>(blockSize, 2), LoggerFactory);
            using var timer = new Momiji.Core.Timer();
            using var audioWaiter = new Waiter(timer, audioInterval, ct);
            using var vst = new AudioMaster<float>(Param.SamplingRate, blockSize, LoggerFactory, timer, DllManager);
            using var toPcm = new ToPcm<float>(LoggerFactory, timer);
            var effect = vst.AddEffect(Param.EffectName);

            using var wave = new WaveOutFloat(
                0,
                2,
                (uint)Param.SamplingRate,
                WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT | WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT,
                LoggerFactory,
                timer,
                pcmPool);

            var options = new ExecutionDataflowBlockOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = 1
            };

            var vstBlock =
                new TransformBlock<VstBuffer<float>, PcmBuffer<float>>(buffer =>
                {
                    var pcmTask = pcmPool.ReceiveAsync(ct);

                    buffer.Log.Clear();
                    audioWaiter.Wait();
                    //VST
                    var nowTime = timer.USecDouble;
                    effect.ProcessEvent(nowTime, MidiEventInput);
                    effect.ProcessReplacing(nowTime, buffer);

                    //trans
                    var pcm = pcmTask.Result;
                    toPcm.Execute(buffer, pcm);
                    vstBufferPool.SendAsync(buffer);
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
                var any = Task.WhenAny(taskSet);
                await any.ConfigureAwait(false);
                any.Wait();
                var task = any.Result;
                taskSet.Remove(task);
                if (task.IsFaulted)
                {
                    ProcessCancel.Cancel();
                    Logger.LogError(task.Exception, "Process Exception");
                }
            }
        }
    }
}