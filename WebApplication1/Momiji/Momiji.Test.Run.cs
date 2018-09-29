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
        bool Start(Param param);
        bool Stop();

        bool Start2(Param param);
        void Note(MIDIMessageEvent[] midiMessage);

    }

    public class Param
    {
        public int bufferCount = 3;

        public int width = 128;
        public int height = 72;
        public int targetBitrate = 2_000_000;
        public float maxFrameRate = 30.0f;
        public int intraFrameIntervalUs = 1_000_000;
        
        public string effectName = "Synth1 VST.dll";
        //public string effectName = "Dexed.dll";
        public int samplingRate = 48000;
        public float sampleLength = 0.06f;
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

        private CancellationTokenSource processCancel;
        private Task processTask;
        private BufferBlock<MIDIMessageEvent> midiEventInput = new BufferBlock<MIDIMessageEvent>();

        public Runner(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            Configuration = configuration;
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Runner>();

            StreamKey = Configuration["MIXER_STREAM_KEY"];
        }

        public bool Start(Param param)
        {
            if (processCancel != null)
            {
                return false;
            }
            processCancel = new CancellationTokenSource();
            processTask = Loop3(param, processCancel);

            processTask.ContinueWith((result) => {
                Stop();
                Start(param);
            });

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

        private async Task Loop1(Param param, CancellationTokenSource processCancel)
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var blockSize = (int)(param.samplingRate * param.sampleLength);

                    var audioInterval = 1_000_000.0 * param.sampleLength;
                    var videoInterval = 1_000_000.0 / param.maxFrameRate;
                    
                    using (var timer = new Core.Timer())
                    using (var audioWaiter = new Waiter(timer, audioInterval, ct))
                    using (var videoWaiter = new Waiter(timer, videoInterval, ct))
                    using (var vstBufferPool = new BufferPool<VstBuffer<float>>(param.bufferCount, () => { return new VstBuffer<float>(blockSize, 2); }))
                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(param.bufferCount, () => { return new PcmBuffer<float>(blockSize, 2); }))
                    using (var audioPool = new BufferPool<OpusOutputBuffer>(param.bufferCount, () => { return new OpusOutputBuffer(5000); }))
                    using (var pcmDummyPool = new BufferPool<PcmBuffer<float>>(param.bufferCount, () => { return new PcmBuffer<float>(blockSize, 2); }))
                    using (var bmpPool = new BufferPool<H264InputBuffer>(param.bufferCount, () => { return new H264InputBuffer(param.width, param.height); }))
                    using (var videoPool = new BufferPool<H264OutputBuffer>(param.bufferCount, () => { return new H264OutputBuffer(200000); }))
                    using (var vst = new AudioMaster<float>(param.samplingRate, blockSize, LoggerFactory, timer))
                    //using (var toPcm = new ToPcm<float>(LoggerFactory, timer))
                    using (var opus = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, LoggerFactory, timer))
                    using (var fft = new FFTEncoder(param.width, param.height, param.maxFrameRate, LoggerFactory, timer))
                    using (var h264 = new H264Encoder(param.width, param.height, param.targetBitrate, param.maxFrameRate, LoggerFactory, timer))
                    {
                        var vstToPcmOutput = vstBufferPool.MakeBufferBlock();
                        var pcmToOpusOutput = pcmPool.MakeBufferBlock();
                        var audioToFtlInput = audioPool.MakeBufferBlock();

                        var pcmToBmpOutput = pcmDummyPool.MakeBufferBlock();
                        var bmpToVideoOutput = bmpPool.MakeBufferBlock();
                        var videoToFtlInput = videoPool.MakeBufferBlock();

                        var effect = vst.AddEffect(param.effectName);

                        using (var ftl = new FtlIngest(StreamKey, LoggerFactory, timer, processCancel))
                        {
                            ftl.Connect();

                            var taskSet = new HashSet<Task>();

                            {
                                var audioWorkerBlock =
                                    new ActionBlock<VstBuffer<float>>(buffer =>
                                    {
                                        buffer.Log.Clear();
                                        buffer.Log.Add("[audio] start pcm input get", timer.USecDouble);
                                        var pcm = pcmToOpusOutput.Receive(ct);
                                        buffer.Log.Add("[audio] end pcm input get", timer.USecDouble);
                                        audioWaiter.Wait();
                                        buffer.Log.Add("[audio] start", timer.USecDouble);

                                        //VST
                                        effect.Execute(buffer, pcm, midiEventInput);
                                        vstToPcmOutput.Post(buffer);

                                        //PCM

                                        //OPUS
                                        buffer.Log.Add("[audio] start ftl input get", timer.USecDouble);
                                        var audio = audioToFtlInput.Receive(ct);
                                        buffer.Log.Add("[audio] end ftl input get", timer.USecDouble);
                                        opus.Execute(pcm, audio);
                                        pcmToOpusOutput.Post(pcm);

                                        //FTL
                                        ftl.Execute(audio);
                                        audioToFtlInput.Post(audio);
                                    },
                                    new ExecutionDataflowBlockOptions
                                    {
                                        CancellationToken = ct,
                                        MaxDegreeOfParallelism = 1
                                    });
                                taskSet.Add(audioWorkerBlock.Completion);
                                vstToPcmOutput.LinkTo(audioWorkerBlock);
                            }

                            {
                                var intraFrameCount = 0.0;
                                var videoWorkerBlock =
                                    new ActionBlock<PcmBuffer<float>>(buffer =>
                                    {
                                        buffer.Log.Clear();
                                        var bmp = bmpToVideoOutput.Receive(ct);
                                        videoWaiter.Wait();
                                        buffer.Log.Add("[video] start", timer.USecDouble);

                                        //FFT
                                        fft.Execute(buffer, bmp);
                                        pcmToBmpOutput.Post(buffer);

                                        //YUV

                                        //H264
                                        buffer.Log.Add("[video] start ftl input get", timer.USecDouble);
                                        var video = videoToFtlInput.Receive(ct);
                                        buffer.Log.Add("[video] end ftl input get", timer.USecDouble);
                                        var insertIntraFrame = (intraFrameCount <= 0);
                                        h264.Execute(bmp, video, insertIntraFrame);
                                        bmpToVideoOutput.Post(bmp);
                                        if (insertIntraFrame)
                                        {
                                            intraFrameCount = param.intraFrameIntervalUs;
                                        }
                                        intraFrameCount -= videoInterval;

                                        //FTL
                                        ftl.Execute(video);
                                        videoToFtlInput.Post(video);
                                    },
                                    new ExecutionDataflowBlockOptions
                                    {
                                        CancellationToken = ct,
                                        MaxDegreeOfParallelism = 1
                                    });
                                taskSet.Add(videoWorkerBlock.Completion);
                                pcmToBmpOutput.LinkTo(videoWorkerBlock);
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

        private async Task Loop3(Param param, CancellationTokenSource processCancel)
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var blockSize = (int)(param.samplingRate * param.sampleLength);
                    
                    var audioInterval = 1_000_000.0 * param.sampleLength;
                    var videoInterval = 1_000_000.0 / param.maxFrameRate;

                    //var bufferCount = 2;

                    using (var timer = new Core.Timer())
                    using (var audioWaiter = new Waiter(timer, audioInterval, ct))
                    using (var videoWaiter = new Waiter(timer, videoInterval, ct))
                    using (var vstBufferPool = new BufferPool<VstBuffer<float>>(param.bufferCount, () => { return new VstBuffer<float>(blockSize, 2); }))
                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(param.bufferCount, () => { return new PcmBuffer<float>(blockSize, 2); }))
                    using (var audioPool = new BufferPool<OpusOutputBuffer>(param.bufferCount * 5, () => { return new OpusOutputBuffer(5000); }))
                    using (var pcmDummyPool = new BufferPool<PcmBuffer<float>>(param.bufferCount, () => { return new PcmBuffer<float>(blockSize, 2); }))
                    using (var bmpPool = new BufferPool<H264InputBuffer>(param.bufferCount, () => { return new H264InputBuffer(param.width, param.height); }))
                    using (var videoPool = new BufferPool<H264OutputBuffer>(param.bufferCount * 2, () => { return new H264OutputBuffer(200000); }))
                    using (var vst = new AudioMaster<float>(param.samplingRate, blockSize, LoggerFactory, timer))
                    //using (var toPcm = new ToPcm<float>(LoggerFactory, timer))
                    using (var opus = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, LoggerFactory, timer))
                    using (var fft = new FFTEncoder(param.width, param.height, param.maxFrameRate, LoggerFactory, timer))
                    using (var h264 = new H264Encoder(param.width, param.height, param.targetBitrate, param.maxFrameRate, LoggerFactory, timer))
                    {
                        var vstToPcmOutput = vstBufferPool.MakeBufferBlock();
                        var pcmToOpusOutput = pcmPool.MakeBufferBlock();
                        var audioToFtlInput = audioPool.MakeBufferBlock();

                        var pcmToBmpOutput = pcmDummyPool.MakeBufferBlock();
                        var bmpToVideoOutput = bmpPool.MakeBufferBlock();
                        var videoToFtlInput = videoPool.MakeBufferBlock();
                        
                        var effect = vst.AddEffect(param.effectName);

                        using (var ftl = new FtlIngest(StreamKey, LoggerFactory, timer, processCancel, true))
                        {
                            ftl.Connect();

                            var taskSet = new HashSet<Task>();

                            {
                                var vstBlock =
                                    new TransformBlock<VstBuffer<float>, PcmBuffer<float>>(buffer =>
                                    {
                                        buffer.Log.Clear();
                                        var pcm = pcmToOpusOutput.Receive(ct);
                                        audioWaiter.Wait();
                                        buffer.Log.Add("[audio] start", timer.USecDouble);

                                        //VST
                                        effect.Execute(buffer, pcm, midiEventInput);
                                        vstToPcmOutput.Post(buffer);
                                        return pcm;
                                    },
                                    new ExecutionDataflowBlockOptions
                                    {
                                        CancellationToken = ct,
                                        MaxDegreeOfParallelism = 1
                                    });
                                taskSet.Add(vstBlock.Completion);
                                vstToPcmOutput.LinkTo(vstBlock);

                                var opusBlock =
                                    new TransformBlock<PcmBuffer<float>, OpusOutputBuffer>(buffer =>
                                    {
                                        buffer.Log.Add("[audio] opus input get", timer.USecDouble);
                                        var audio = audioToFtlInput.Receive(ct);
                                        buffer.Log.Add("[audio] ftl output get", timer.USecDouble);
                                        opus.Execute(buffer, audio);
                                        pcmToOpusOutput.Post(buffer);
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
                                        audioToFtlInput.Post(buffer);
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
                                        var bmp = bmpToVideoOutput.Receive(ct);
                                        bmp.Log.Clear();

                                        videoWaiter.Wait();
                                        buffer.Log.Add("[video] start", timer.USecDouble);

                                        //FFT
                                        fft.Execute(buffer, bmp);
                                        pcmToBmpOutput.Post(buffer);
                                        return bmp;
                                    },
                                    new ExecutionDataflowBlockOptions
                                    {
                                        CancellationToken = ct,
                                        MaxDegreeOfParallelism = 1
                                    });
                                taskSet.Add(fftBlock.Completion);
                                pcmToBmpOutput.LinkTo(fftBlock);

                                var h264Block =
                                    new TransformBlock<H264InputBuffer, H264OutputBuffer>(buffer =>
                                    {
                                        //H264
                                        buffer.Log.Add("[video] h264 input get", timer.USecDouble);
                                        var video = videoToFtlInput.Receive(ct);
                                        buffer.Log.Add("[video] ftl output get", timer.USecDouble);
                                        var insertIntraFrame = (intraFrameCount <= 0);
                                        h264.Execute(buffer, video, insertIntraFrame);
                                        bmpToVideoOutput.Post(buffer);
                                        if (insertIntraFrame)
                                        {
                                            intraFrameCount = param.intraFrameIntervalUs;
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
                                        videoToFtlInput.Post(buffer);
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

        public bool Start2(Param param)
        {
            if (processCancel != null)
            {
                return false;
            }
            processCancel = new CancellationTokenSource();
            processTask = Loop2(param, processCancel);
            return true;
        }

        private async Task Loop2(Param param, CancellationTokenSource processCancel)
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var blockSize = (int)(param.samplingRate * param.sampleLength);
                    var audioInterval = 1_000_000.0 * param.sampleLength;

                    using (var vstBufferPool = new BufferPool<VstBuffer<float>>(param.bufferCount, () => { return new VstBuffer<float>(blockSize, 2); }))
                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(param.bufferCount, () => { return new PcmBuffer<float>(blockSize, 2); }))
                    {
                        var vstToPcmInput = vstBufferPool.MakeBufferBlock();
                        var pcmToWaveOutput = pcmPool.MakeBufferBlock();

                        using (var timer = new Core.Timer())
                        using (var w = new Waiter(timer, audioInterval, ct))
                        using (var vst = new AudioMaster<float>(param.samplingRate, blockSize, LoggerFactory, timer))
                        using (var wave = new WaveOutFloat(
                            0,
                            2,
                            (uint)param.samplingRate,
                            WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT | WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT,
                            LoggerFactory,
                            timer))
                        {
                            var effect = vst.AddEffect(param.effectName);

                            var taskSet = new HashSet<Task>();
                            
                            var vstAction =
                                new TransformBlock<VstBuffer<float>, PcmBuffer<float>>(buffer =>
                                {
                                    buffer.Log.Clear();
                                    var pcm = pcmToWaveOutput.Receive(ct);
                                    w.Wait();

                                    //VST
                                    effect.Execute(buffer, pcm, midiEventInput);
                                    vstToPcmInput.Post(buffer);
                                    return pcm;
                                },
                                new ExecutionDataflowBlockOptions
                                {
                                    CancellationToken = ct,
                                    MaxDegreeOfParallelism = 1
                                });
                            taskSet.Add(vstAction.Completion);
                            vstToPcmInput.LinkTo(vstAction);

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
                                pcmToWaveOutput,
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

        private async Task Loop22(Param param, CancellationTokenSource processCancel)
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var blockSize = (int)(param.samplingRate * param.sampleLength);
                    var audioInterval = 1_000_000.0 * param.sampleLength;

                    using (var vstBufferPool = new BufferPool<VstBuffer<float>>(param.bufferCount, () => { return new VstBuffer<float>(blockSize, 2); }))
                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(param.bufferCount, () => { return new PcmBuffer<float>(blockSize, 2); }))
                    {
                        var vstToPcmOutput = vstBufferPool.MakeBufferBlock();
                        var pcmToWaveOutput = pcmPool.MakeBufferBlock();
                        
                        using (var timer = new Core.Timer())
                        using (var w = new Waiter(timer, audioInterval, ct))
                        using (var vst = new AudioMaster<float>(param.samplingRate, blockSize, LoggerFactory, timer))
                        //using (var toPcm = new ToPcm<float>(LoggerFactory, timer))
                        using (var wave = new WaveOutFloat(
                            0,
                            2,
                            (uint)param.samplingRate,
                            WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT | WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT,
                            LoggerFactory,
                            timer))
                        {
                            var effect = vst.AddEffect(param.effectName);

                            var taskSet = new HashSet<Task>();

                            var workerBlock =
                                new ActionBlock<VstBuffer<float>>(buffer =>
                                {
                                    buffer.Log.Clear();
                                    var pcm = pcmToWaveOutput.Receive(ct);
                                    w.Wait();

                                    //VST
                                    effect.Execute(buffer, pcm, midiEventInput);
                                    vstToPcmOutput.Post(buffer);

                                    //WAVEOUT
                                    wave.Execute(pcm, ct);
                                },
                                new ExecutionDataflowBlockOptions
                                {
                                    CancellationToken = ct,
                                    MaxDegreeOfParallelism = 1
                                });
                            taskSet.Add(workerBlock.Completion);
                            vstToPcmOutput.LinkTo(workerBlock);

                            taskSet.Add(wave.Release(
                                pcmToWaveOutput,
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
            foreach (var item in midiMessage)
            {
                Logger.LogInformation($"note {DateTimeOffset.FromUnixTimeMilliseconds((long)item.receivedTime).ToUniversalTime()}");

                var vstEvent = new VstMidiEvent
                {
                    type = VstEvent.VstEventTypes.kVstMidiType,
                    byteSize = Marshal.SizeOf<VstMidiEvent>(),
                    deltaFrames = 0,
                    flags = VstMidiEvent.VstMidiEventFlags.kVstMidiEventIsRealtime,

                    midiData0 = item.data[0],
                    midiData1 = item.data[1],
                    midiData2 = item.data[2],
                    midiData3 = 0x00
                };
                midiEventInput.Post(item);
            }
        }
    }
}