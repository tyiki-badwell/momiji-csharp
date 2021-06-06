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
    public class Logic4
    {
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private IDllManager DllManager { get; }
        private string StreamKey { get; }
        private string IngestHostname { get; }
        private string CaInfoPath { get; }
        private Param Param { get; }

        private CancellationToken Ct { get; }

        private BufferBlock<MIDIMessageEvent2> MidiEventInput { get; }
        private BufferBlock<MIDIMessageEvent2> MidiEventOutput { get; }

        private HashSet<Task> TaskSet { get; }

        public Logic4(
            IConfiguration configuration, 
            ILoggerFactory loggerFactory, 
            IDllManager dllManager, 
            Param param, 
            BufferBlock<MIDIMessageEvent2> midiEventInput,
            BufferBlock<MIDIMessageEvent2> midiEventOutput,
            HashSet<Task> taskSet,
            CancellationToken ct
        )
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Runner>();
            DllManager = dllManager;
            Param = param;
            Ct = ct;
            MidiEventInput = midiEventInput;
            MidiEventOutput = midiEventOutput;
            TaskSet = taskSet;

            StreamKey = Configuration["MIXER_STREAM_KEY"];
            IngestHostname = Configuration["MIXER_INGEST_HOSTNAME"];

            CaInfoPath =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "lib",
                    "cacert.pem"
                );
        }

        public void Run()
        {
            if (Param.Local)
            {
                Loop5();
            }
            else
            {
                Loop4();
            }
        }
        private void Loop5()
        {
            var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
            Logger.LogInformation($"[loop5] blockSize {blockSize}");

            var audioInterval = 1_000_000.0 * Param.SampleLength;
            Logger.LogInformation($"[loop5] audioInterval {audioInterval}");
            var videoInterval = 1_000_000.0 / Param.MaxFrameRate;
            Logger.LogInformation($"[loop5] videoInterval {videoInterval}");

            using var timer = new Momiji.Core.Timer();
            using var audioWaiter = new Waiter(timer, audioInterval, Ct);
            using var videoWaiter = new Waiter(timer, videoInterval, Ct);
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

            var options = new ExecutionDataflowBlockOptions
            {
                CancellationToken = Ct,
                MaxDegreeOfParallelism = 1,
                MaxMessagesPerTask = DataflowBlockOptions.Unbounded
            };

            {
                var audioBlock =
                    new ActionBlock<VstBuffer<float>>(buffer =>
                    {
                        var pcmTask = pcmPool.ReceiveAsync(Ct);
                        var audioTask = audioPool.ReceiveAsync(Ct);

                        buffer.Log.Clear();
                        audioWaiter.Wait();
                        buffer.Log.Add("[audio] start", timer.USecDouble);

                        //VST
                        var nowTime = timer.USecDouble;
                        effect.ProcessEvent(nowTime, MidiEventInput);
                        effect.ProcessReplacing(nowTime, buffer);

                        //trans
                        var pcm = pcmTask.Result;
                        buffer.Log.Add("[audio] opus input get", timer.USecDouble);
                        toPcm.Execute(buffer, pcm);
                        vstBufferPool.SendAsync(buffer);

                        var audio = audioTask.Result;
                        pcm.Log.Add("[audio] opus output get", timer.USecDouble);
                        opus.Execute(pcm, audio);

                        pcmDrowPool.SendAsync(pcm);

                        //FTL
                        //   ftl.Execute(audio);

                        audioPool.SendAsync(audio);
                    }, options);
                TaskSet.Add(audioBlock.Completion);
                vstBufferPool.LinkTo(audioBlock);
            }

            {
                var midiDataStoreBlock =
                    new ActionBlock<MIDIMessageEvent2>(buffer =>
                    {
                        fft.Receive(buffer);
                    }, options);
                TaskSet.Add(midiDataStoreBlock.Completion);
                MidiEventOutput.LinkTo(midiDataStoreBlock);

                var pcmDataStoreBlock =
                    new ActionBlock<PcmBuffer<float>>(buffer =>
                    {
                        fft.Receive(buffer);
                        pcmPool.SendAsync(buffer);
                    }, options);
                TaskSet.Add(pcmDataStoreBlock.Completion);
                pcmDrowPool.LinkTo(pcmDataStoreBlock);

                var intraFrameCount = 0.0;

                var videoBlock =
                    new ActionBlock<H264InputBuffer>(buffer =>
                    {
                        var videoTask = videoPool.ReceiveAsync(Ct);

                        buffer.Log.Clear();

                        videoWaiter.Wait();
                        buffer.Log.Add("[video] start", timer.USecDouble);

                        //FFT
                        fft.Execute(buffer);

                        //H264
                        var video = videoTask.Result;
                        buffer.Log.Add("[video] h264 output get", timer.USecDouble);

                        var insertIntraFrame = (intraFrameCount <= 0);
                        h264.Execute(buffer, video, insertIntraFrame);

                        bmpPool.SendAsync(buffer);
                        if (insertIntraFrame)
                        {
                            intraFrameCount = Param.IntraFrameIntervalUs;
                        }
                        intraFrameCount -= videoInterval;

                        //FTL
                        //ftl.Execute(video);

                        videoPool.SendAsync(video);
                    }, options);
                TaskSet.Add(videoBlock.Completion);
                bmpPool.LinkTo(videoBlock);
            }
        }

        private void Loop4()
        {
            var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
            Logger.LogInformation($"[loop4] blockSize {blockSize}");

            var audioInterval = 1_000_000.0 * Param.SampleLength;
            Logger.LogInformation($"[loop4] audioInterval {audioInterval}");
            var videoInterval = 1_000_000.0 / Param.MaxFrameRate;
            Logger.LogInformation($"[loop4] videoInterval {videoInterval}");

            using var timer = new Momiji.Core.Timer();
            using var audioWaiter = new Waiter(timer, audioInterval, Ct);
            using var videoWaiter = new Waiter(timer, videoInterval, Ct);
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
                CancellationToken = Ct,
                MaxDegreeOfParallelism = 1,
                MaxMessagesPerTask = DataflowBlockOptions.Unbounded
            };

            {
                var audioBlock =
                    new ActionBlock<VstBuffer<float>>(buffer =>
                    {
                        var pcmTask = pcmPool.ReceiveAsync(Ct);
                        var audioTask = audioPool.ReceiveAsync(Ct);

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

                        var audio = audioTask.Result;
                        pcm.Log.Add("[audio] opus output get", timer.USecDouble);
                        opus.Execute(pcm, audio);

                        pcmDrowPool.SendAsync(pcm);

                        //FTL
                        ftl.Execute(audio);
                        audioPool.SendAsync(audio);
                    }, options);
                TaskSet.Add(audioBlock.Completion);
                vstBufferPool.LinkTo(audioBlock);
            }

            {
                var midiDataStoreBlock =
                    new ActionBlock<MIDIMessageEvent2>(buffer =>
                    {
                        fft.Receive(buffer);
                    }, options);
                TaskSet.Add(midiDataStoreBlock.Completion);
                MidiEventOutput.LinkTo(midiDataStoreBlock);

                var pcmDataStoreBlock =
                    new ActionBlock<PcmBuffer<float>>(buffer =>
                    {
                        fft.Receive(buffer);
                        pcmPool.SendAsync(buffer);
                    }, options);
                TaskSet.Add(pcmDataStoreBlock.Completion);
                pcmDrowPool.LinkTo(pcmDataStoreBlock);

                var intraFrameCount = 0.0;

                var videoBlock =
                    new ActionBlock<H264InputBuffer>(buffer =>
                    {
                        var videoTask = videoPool.ReceiveAsync(Ct);

                        buffer.Log.Clear();

                        videoWaiter.Wait();
                        buffer.Log.Add("[video] start", timer.USecDouble);

                        //FFT
                        fft.Execute(buffer);

                        //H264
                        var video = videoTask.Result;
                        buffer.Log.Add("[video] h264 output get", timer.USecDouble);
                        var insertIntraFrame = (intraFrameCount <= 0);
                        h264.Execute(buffer, video, insertIntraFrame);
                        bmpPool.SendAsync(buffer);
                        if (insertIntraFrame)
                        {
                            intraFrameCount = Param.IntraFrameIntervalUs;
                        }
                        intraFrameCount -= videoInterval;

                        //FTL
                        ftl.Execute(video);
                        videoPool.SendAsync(video);
                    }, options);
                TaskSet.Add(videoBlock.Completion);
                bmpPool.LinkTo(videoBlock);
            }
        }
    }
}