using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core;
using Momiji.Core.FFT;
using Momiji.Core.Ftl;
using Momiji.Core.H264;
using Momiji.Core.Opus;
using Momiji.Core.Vst;
using Momiji.Core.Wave;
using Momiji.Core.WebMidi;
using Momiji.Interop.Opus;
using Momiji.Interop.Vst;
using Momiji.Interop.Wave;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Test.Run
{
    public interface IRunner
    {
        bool Start();
        bool Stop();

        bool Start2();
        void Note(MIDIMessageEvent[] midiMessage);

    }

    public class Param
    {
        public int BufferCount { get; set; }

        public int Width { get; set; }
        public int Height { get; set; }
        public int TargetBitrate { get; set; }
        public float MaxFrameRate { get; set; }
        public int IntraFrameIntervalUs { get; set; }

        public string EffectName { get; set; }
        //public string effectName = "Dexed.dll";
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

    public class Runner : IRunner
    {
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private string StreamKey { get; }
        private Param Param { get; }

        private CancellationTokenSource processCancel;
        private Task processTask;
        private BufferBlock<MIDIMessageEvent> midiEventInput = new BufferBlock<MIDIMessageEvent>();

        public Runner(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            Configuration = configuration;
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Runner>();

            var param = new Param();
            Configuration.GetSection("Param").Bind(param);
            Param = param;

            StreamKey = Configuration["MIXER_STREAM_KEY"];
        }

        public bool Start()
        {
            if (processCancel != null)
            {
                return false;
            }
            processCancel = new CancellationTokenSource();
            processTask = Loop3(processCancel);

            return true;
        }


        public bool Stop()
        {
            if (processCancel == null)
            {
                return false;
            }

            processCancel.Cancel();
            try
            {
                processTask.Wait();
            }
            catch (AggregateException e)
            {
                foreach (var v in e.InnerExceptions)
                {
                    Logger.LogInformation($"[home] Process Exception:{e.Message} {v.Message}");
                }
            }
            finally
            {
                processTask = null;
                processCancel.Dispose();
                processCancel = null;
            }

            return true;
        }

        private async Task Loop1(CancellationTokenSource processCancel)
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var blockSize = (int)(Param.SamplingRate * Param.SampleLength);

                    var audioInterval = 1_000_000.0 * Param.SampleLength;
                    var videoInterval = 1_000_000.0 / Param.MaxFrameRate;
                    
                    using (var timer = new Core.Timer())
                    using (var audioWaiter = new Waiter(timer, audioInterval, ct))
                    using (var videoWaiter = new Waiter(timer, videoInterval, ct))
                    using (var vstBufferPool = new BufferPool<VstBuffer<float>>(Param.BufferCount, () => { return new VstBuffer<float>(blockSize, 2); }, LoggerFactory))
                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => { return new PcmBuffer<float>(blockSize, 2); }, LoggerFactory))
                    using (var audioPool = new BufferPool<OpusOutputBuffer>(Param.BufferCount, () => { return new OpusOutputBuffer(5000); }, LoggerFactory))
                    using (var pcmDummyPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => { return new PcmBuffer<float>(blockSize, 2); }, LoggerFactory))
                    using (var bmpPool = new BufferPool<H264InputBuffer>(Param.BufferCount, () => { return new H264InputBuffer(Param.Width, Param.Height); }, LoggerFactory))
                    using (var videoPool = new BufferPool<H264OutputBuffer>(Param.BufferCount, () => { return new H264OutputBuffer(200000); }, LoggerFactory))
                    using (var vst = new AudioMaster<float>(Param.SamplingRate, blockSize, LoggerFactory, timer))
                    //using (var toPcm = new ToPcm<float>(LoggerFactory, timer))
                    using (var opus = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, LoggerFactory, timer))
                    using (var fft = new FFTEncoder(Param.Width, Param.Height, Param.MaxFrameRate, LoggerFactory, timer))
                    using (var h264 = new H264Encoder(Param.Width, Param.Height, Param.TargetBitrate, Param.MaxFrameRate, LoggerFactory, timer))
                    {
                        var effect = vst.AddEffect(Param.EffectName);

                        using (var ftl = new FtlIngest(StreamKey, LoggerFactory, timer))
                        {
                            ftl.Connect();

                            var taskSet = new HashSet<Task>();

                            {
                                var audioWorkerBlock =
                                    new ActionBlock<VstBuffer<float>>(buffer =>
                                    {
                                        buffer.Log.Clear();
                                        buffer.Log.Add("[audio] start pcm input get", timer.USecDouble);
                                        var pcm = pcmPool.Receive(ct);
                                        buffer.Log.Add("[audio] end pcm input get", timer.USecDouble);
                                        audioWaiter.Wait();
                                        buffer.Log.Add("[audio] start", timer.USecDouble);

                                        //VST
                                        effect.Execute(buffer, pcm, midiEventInput);
                                        vstBufferPool.Post(buffer);

                                        //PCM

                                        //OPUS
                                        buffer.Log.Add("[audio] start ftl input get", timer.USecDouble);
                                        var audio = audioPool.Receive(ct);
                                        buffer.Log.Add("[audio] end ftl input get", timer.USecDouble);
                                        opus.Execute(pcm, audio);
                                        pcmPool.Post(pcm);

                                        //FTL
                                        ftl.Execute(audio);
                                        audioPool.Post(audio);
                                    },
                                    new ExecutionDataflowBlockOptions
                                    {
                                        CancellationToken = ct,
                                        MaxDegreeOfParallelism = 1
                                    });
                                taskSet.Add(audioWorkerBlock.Completion);
                                vstBufferPool.LinkTo(audioWorkerBlock);
                            }

                            {
                                var intraFrameCount = 0.0;
                                var videoWorkerBlock =
                                    new ActionBlock<PcmBuffer<float>>(buffer =>
                                    {
                                        buffer.Log.Clear();
                                        var bmp = bmpPool.Receive(ct);
                                        videoWaiter.Wait();
                                        buffer.Log.Add("[video] start", timer.USecDouble);

                                        //FFT
                                        fft.Execute(buffer, bmp);
                                        pcmDummyPool.Post(buffer);

                                        //YUV

                                        //H264
                                        buffer.Log.Add("[video] start ftl input get", timer.USecDouble);
                                        var video = videoPool.Receive(ct);
                                        buffer.Log.Add("[video] end ftl input get", timer.USecDouble);
                                        var insertIntraFrame = (intraFrameCount <= 0);
                                        h264.Execute(bmp, video, insertIntraFrame);
                                        bmpPool.Post(bmp);
                                        if (insertIntraFrame)
                                        {
                                            intraFrameCount = Param.IntraFrameIntervalUs;
                                        }
                                        intraFrameCount -= videoInterval;

                                        //FTL
                                        ftl.Execute(video);
                                        videoPool.Post(video);
                                    },
                                    new ExecutionDataflowBlockOptions
                                    {
                                        CancellationToken = ct,
                                        MaxDegreeOfParallelism = 1
                                    });
                                taskSet.Add(videoWorkerBlock.Completion);
                                pcmDummyPool.LinkTo(videoWorkerBlock);
                            }

                            while (taskSet.Count > 0)
                            {
                                var any = Task.WhenAny(taskSet);
                                any.ConfigureAwait(false);
                                any.Wait();
                                var task = any.Result;
                                taskSet.Remove(task);
                                if (task.IsFaulted)
                                {
                                    processCancel.Cancel();
                                    foreach (var v in task.Exception.InnerExceptions)
                                    {
                                        Logger.LogInformation($"Process Exception:{task.Exception.Message} {v.Message}");
                                    }
                                }
                            }
                        }
                    }
                });
            }
            finally
            {
                Logger.LogInformation("main loop end");
            }
        }

        private async Task Loop3(CancellationTokenSource processCancel)
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
                    
                    var audioInterval = 1_000_000.0 * Param.SampleLength;
                    var videoInterval = 1_000_000.0 / Param.MaxFrameRate;

                    //var bufferCount = 2;

                    using (var timer = new Core.Timer())
                    using (var audioWaiter = new Waiter(timer, audioInterval, ct))
                    using (var videoWaiter = new Waiter(timer, videoInterval, ct))
                    using (var vstBufferPool = new BufferPool<VstBuffer<float>>(Param.BufferCount, () => { return new VstBuffer<float>(blockSize, 2); }, LoggerFactory))
                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => { return new PcmBuffer<float>(blockSize, 2); }, LoggerFactory))
                    using (var audioPool = new BufferPool<OpusOutputBuffer>(Param.BufferCount * 5, () => { return new OpusOutputBuffer(5000); }, LoggerFactory))
                    using (var pcmDummyPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => { return new PcmBuffer<float>(blockSize, 2); }, LoggerFactory))
                    using (var bmpPool = new BufferPool<H264InputBuffer>(Param.BufferCount, () => { return new H264InputBuffer(Param.Width, Param.Height); }, LoggerFactory))
                    using (var videoPool = new BufferPool<H264OutputBuffer>(Param.BufferCount * 2, () => { return new H264OutputBuffer(200000); }, LoggerFactory))
                    using (var vst = new AudioMaster<float>(Param.SamplingRate, blockSize, LoggerFactory, timer))
                    //using (var toPcm = new ToPcm<float>(LoggerFactory, timer))
                    using (var opus = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, LoggerFactory, timer))
                    using (var fft = new FFTEncoder(Param.Width, Param.Height, Param.MaxFrameRate, LoggerFactory, timer))
                    using (var h264 = new H264Encoder(Param.Width, Param.Height, Param.TargetBitrate, Param.MaxFrameRate, LoggerFactory, timer))
                    {
                        var effect = vst.AddEffect(Param.EffectName);

                        using (var ftl = new FtlIngest(StreamKey, LoggerFactory, timer, true))
                        {
                            ftl.Connect();

                            var taskSet = new HashSet<Task>();

                            {
                                var vstBlock =
                                    new TransformBlock<VstBuffer<float>, PcmBuffer<float>>(buffer =>
                                    {
                                        buffer.Log.Clear();
                                        var pcm = pcmPool.Receive(ct);
                                        audioWaiter.Wait();
                                        buffer.Log.Add("[audio] start", timer.USecDouble);

                                        //VST
                                        effect.Execute(buffer, pcm, midiEventInput);
                                        vstBufferPool.Post(buffer);
                                        return pcm;
                                    },
                                    new ExecutionDataflowBlockOptions
                                    {
                                        CancellationToken = ct,
                                        MaxDegreeOfParallelism = 1
                                    });
                                taskSet.Add(vstBlock.Completion);
                                vstBufferPool.LinkTo(vstBlock);

                                var opusBlock =
                                    new TransformBlock<PcmBuffer<float>, OpusOutputBuffer>(buffer =>
                                    {
                                        buffer.Log.Add("[audio] opus input get", timer.USecDouble);
                                        var audio = audioPool.Receive(ct);
                                        buffer.Log.Add("[audio] ftl output get", timer.USecDouble);
                                        opus.Execute(buffer, audio);
                                        pcmPool.Post(buffer);
                                        return audio;
                                    },
                                    new ExecutionDataflowBlockOptions
                                    {
                                        CancellationToken = ct,
                                        MaxDegreeOfParallelism = 1
                                    });
                                taskSet.Add(opusBlock.Completion);
                                vstBlock.LinkTo(opusBlock);

                                var ftlBlock =
                                    new ActionBlock<OpusOutputBuffer>(buffer =>
                                    {
                                        //FTL
                                        buffer.Log.Add("[audio] ftl input get", timer.USecDouble);
                                        ftl.Execute(buffer);
                                        audioPool.Post(buffer);
                                    },
                                    new ExecutionDataflowBlockOptions
                                    {
                                        CancellationToken = ct,
                                        MaxDegreeOfParallelism = 1
                                    });
                                taskSet.Add(ftlBlock.Completion);
                                opusBlock.LinkTo(ftlBlock);
                            }

                            {
                                var intraFrameCount = 0.0;

                                var fftBlock =
                                    new TransformBlock<PcmBuffer<float>, H264InputBuffer>(buffer =>
                                    {
                                        buffer.Log.Clear();
                                        var bmp = bmpPool.Receive(ct);
                                        bmp.Log.Clear();

                                        videoWaiter.Wait();
                                        buffer.Log.Add("[video] start", timer.USecDouble);

                                        //FFT
                                        fft.Execute(buffer, bmp);
                                        pcmDummyPool.Post(buffer);
                                        return bmp;
                                    },
                                    new ExecutionDataflowBlockOptions
                                    {
                                        CancellationToken = ct,
                                        MaxDegreeOfParallelism = 1
                                    });
                                taskSet.Add(fftBlock.Completion);
                                pcmDummyPool.LinkTo(fftBlock);

                                var h264Block =
                                    new TransformBlock<H264InputBuffer, H264OutputBuffer>(buffer =>
                                    {
                                        //H264
                                        buffer.Log.Add("[video] h264 input get", timer.USecDouble);
                                        var video = videoPool.Receive(ct);
                                        buffer.Log.Add("[video] ftl output get", timer.USecDouble);
                                        var insertIntraFrame = (intraFrameCount <= 0);
                                        h264.Execute(buffer, video, insertIntraFrame);
                                        bmpPool.Post(buffer);
                                        if (insertIntraFrame)
                                        {
                                            intraFrameCount = Param.IntraFrameIntervalUs;
                                        }
                                        intraFrameCount -= videoInterval;
                                        return video;
                                    },
                                    new ExecutionDataflowBlockOptions
                                    {
                                        CancellationToken = ct,
                                        MaxDegreeOfParallelism = 1
                                    });
                                taskSet.Add(h264Block.Completion);
                                fftBlock.LinkTo(h264Block);

                                var ftlBlock =
                                    new ActionBlock<H264OutputBuffer>(buffer =>
                                    {
                                        //FTL
                                        buffer.Log.Add("[video] ftl input get", timer.USecDouble);
                                        ftl.Execute(buffer);
                                        videoPool.Post(buffer);
                                    },
                                    new ExecutionDataflowBlockOptions
                                    {
                                        CancellationToken = ct,
                                        MaxDegreeOfParallelism = 1
                                    });
                                taskSet.Add(ftlBlock.Completion);
                                h264Block.LinkTo(ftlBlock);
                            }

                            while (taskSet.Count > 0)
                            {
                                var any = Task.WhenAny(taskSet);
                                any.ConfigureAwait(false);
                                any.Wait();
                                var task = any.Result;
                                taskSet.Remove(task);
                                if (task.IsFaulted)
                                {
                                    processCancel.Cancel();
                                    foreach (var v in task.Exception.InnerExceptions)
                                    {
                                        Logger.LogInformation($"Process Exception:{task.Exception.Message} {v.Message}");
                                    }
                                }
                            }
                        }
                    }
                });
            }
            finally
            {
                Logger.LogInformation("main loop end");
            }
        }

        public bool Start2()
        {
            if (processCancel != null)
            {
                return false;
            }
            processCancel = new CancellationTokenSource();
            processTask = Loop2(processCancel);
            return true;
        }

        private async Task Loop2(CancellationTokenSource processCancel)
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
                    var audioInterval = 1_000_000.0 * Param.SampleLength;

                    using (var vstBufferPool = new BufferPool<VstBuffer<float>>(Param.BufferCount, () => { return new VstBuffer<float>(blockSize, 2); }, LoggerFactory))
                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => { return new PcmBuffer<float>(blockSize, 2); }, LoggerFactory))
                    {
                        using (var timer = new Core.Timer())
                        using (var w = new Waiter(timer, audioInterval, ct))
                        using (var vst = new AudioMaster<float>(Param.SamplingRate, blockSize, LoggerFactory, timer))
                        using (var wave = new WaveOutFloat(
                            0,
                            2,
                            (uint)Param.SamplingRate,
                            WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT | WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT,
                            LoggerFactory,
                            timer))
                        {
                            var effect = vst.AddEffect(Param.EffectName);

                            var taskSet = new HashSet<Task>();

                            var beforeVstStart = timer.USecDouble;

                            var vstAction =
                                new TransformBlock<VstBuffer<float>, PcmBuffer<float>>(buffer =>
                                {
                                    buffer.Log.Clear();
                                    var beforeReceiveTime = timer.USecDouble;
                                    var pcm = pcmPool.Receive(ct);
                                    buffer.Log.Add("[vst] receive", beforeReceiveTime);
                                    buffer.Log.Add("[vst] receive ok", timer.USecDouble);
                                    w.Wait();
                                    var now = timer.USecDouble;
                                    buffer.Log.Add($"[vst] go {now - beforeVstStart}", now);
                                    beforeVstStart = now;

                                    //VST
                                    effect.Execute(buffer, pcm, midiEventInput);
                                    vstBufferPool.Post(buffer);
                                    return pcm;
                                },
                                new ExecutionDataflowBlockOptions
                                {
                                    CancellationToken = ct,
                                    MaxDegreeOfParallelism = 1
                                });
                            taskSet.Add(vstAction.Completion);
                            vstBufferPool.LinkTo(vstAction);

                            var waveAction =
                                new ActionBlock<PcmBuffer<float>>(buffer =>
                                {
                                    //WAVEOUT
                                    wave.Execute(buffer, ct);
                                },
                                new ExecutionDataflowBlockOptions
                                {
                                    CancellationToken = ct,
                                    MaxDegreeOfParallelism = 2
                                });
                            taskSet.Add(waveAction.Completion);
                            vstAction.LinkTo(waveAction);
                            
                            taskSet.Add(wave.Release(
                                pcmPool,
                                ct
                            ));

                            while (taskSet.Count > 0)
                            {
                                var any = Task.WhenAny(taskSet);
                                any.ConfigureAwait(false);
                                any.Wait();
                                var task = any.Result;
                                taskSet.Remove(task);
                                if (task.IsFaulted)
                                {
                                    processCancel.Cancel();
                                    foreach (var v in task.Exception.InnerExceptions)
                                    {
                                        Logger.LogInformation($"Process Exception:{task.Exception.Message} {v.Message}");
                                    }
                                }
                            }
                        }
                    }
                });
            }
            finally
            {
                Logger.LogInformation("main loop end");
            }
        }

        private async Task Loop22(CancellationTokenSource processCancel)
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
                    var audioInterval = 1_000_000.0 * Param.SampleLength;

                    using (var vstBufferPool = new BufferPool<VstBuffer<float>>(Param.BufferCount, () => { return new VstBuffer<float>(blockSize, 2); }, LoggerFactory))
                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => { return new PcmBuffer<float>(blockSize, 2); }, LoggerFactory))
                    {
                        using (var timer = new Core.Timer())
                        using (var w = new Waiter(timer, audioInterval, ct))
                        using (var vst = new AudioMaster<float>(Param.SamplingRate, blockSize, LoggerFactory, timer))
                        //using (var toPcm = new ToPcm<float>(LoggerFactory, timer))
                        using (var wave = new WaveOutFloat(
                            0,
                            2,
                            (uint)Param.SamplingRate,
                            WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT | WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT,
                            LoggerFactory,
                            timer))
                        {
                            var effect = vst.AddEffect(Param.EffectName);

                            var taskSet = new HashSet<Task>();

                            var workerBlock =
                                new ActionBlock<VstBuffer<float>>(buffer =>
                                {
                                    buffer.Log.Clear();
                                    var pcm = pcmPool.Receive(ct);
                                    w.Wait();

                                    //VST
                                    effect.Execute(buffer, pcm, midiEventInput);
                                    vstBufferPool.Post(buffer);

                                    //WAVEOUT
                                    wave.Execute(pcm, ct);
                                },
                                new ExecutionDataflowBlockOptions
                                {
                                    CancellationToken = ct,
                                    MaxDegreeOfParallelism = 1
                                });
                            taskSet.Add(workerBlock.Completion);
                            vstBufferPool.LinkTo(workerBlock);

                            taskSet.Add(wave.Release(
                                pcmPool,
                                ct
                            ));

                            while (taskSet.Count > 0)
                            {
                                var any = Task.WhenAny(taskSet);
                                any.ConfigureAwait(false);
                                any.Wait();
                                var task = any.Result;
                                taskSet.Remove(task);
                                if (task.IsFaulted)
                                {
                                    processCancel.Cancel();
                                    foreach (var v in task.Exception.InnerExceptions)
                                    {
                                        Logger.LogInformation($"Process Exception:{task.Exception.Message} {v.Message}");
                                    }
                                }
                            }
                        }
                    }
                });
            }
            finally
            {
                Logger.LogInformation("main loop end");
            }
        }

        public void Note(MIDIMessageEvent[] midiMessage)
        {
            List<MIDIMessageEvent> list = new List<MIDIMessageEvent>(midiMessage);
            list.Sort((a, b) => (int)(a.receivedTime - b.receivedTime));
            
            foreach (var item in list)
            {
                Logger.LogInformation($"note {DateTimeOffset.FromUnixTimeMilliseconds((long)item.receivedTime).ToUniversalTime()}");
                midiEventInput.Post(item);
            }
        }
    }
}