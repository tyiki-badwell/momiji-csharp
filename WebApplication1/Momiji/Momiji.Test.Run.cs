using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core;
using Momiji.Core.Ftl;
using Momiji.Core.H264;
using Momiji.Core.Opus;
using Momiji.Core.FFT;
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
using Momiji.Core.Trans;
using System.Diagnostics;

namespace Momiji.Test.Run
{
    public interface IRunner
    {
        bool Start();
        bool Stop();

        bool Start2();
        void Note(MIDIMessageEvent[] midiMessage);
    }

    public class Runner : IRunner
    {
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        private CancellationTokenSource processCancel;
        private Task processTask;
        private BufferBlock<VstMidiEvent> midiEventInput = new BufferBlock<VstMidiEvent>();

        public Runner(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            Configuration = configuration;
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Runner>();
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

            if (processCancel != null)
            {
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

                    var width = 1280;
                    var height = 720;
                    var targetBitrate = 5_000_000;
                    var maxFrameRate = 60.0f;
                    var intraFrameIntervalMs = 1000;

                    var samplingRate = 48000;
                    var sampleLength = 0.01;// 0.06;
                    var blockSize = (int)(samplingRate * sampleLength);

                    var streamKey = Configuration["MIXER_STREAM_KEY"];

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

                    using (var timer = new Core.Timer())
                    using (var vstBufferPool = new BufferPool<VstBuffer<float>>(3, () => { return new VstBuffer<float>(blockSize, 2); }))
                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(3, () => { return new PcmBuffer<float>(blockSize, 2); }))
                    using (var audioPool = new BufferPool<OpusOutputBuffer>(3, () => { return new OpusOutputBuffer(5000); }))
                    using (var bmpPool = new BufferPool<H264InputBuffer>(3, () => { return new H264InputBuffer(width, height); }))
                    using (var videoPool = new BufferPool<H264OutputBuffer>(3, () => { return new H264OutputBuffer(100000); }))
                    using (var vst = new AudioMaster<float>(samplingRate, blockSize, LoggerFactory, timer))
                    //using (var toPcm = new ToPcm<float>(LoggerFactory, timer))
                    using (var opus = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, LoggerFactory, timer))
                    using (var fft = new FFTEncoder(width, height, maxFrameRate, LoggerFactory, timer))
                    using (var h264 = new H264Encoder(width, height, targetBitrate, maxFrameRate, intraFrameIntervalMs, LoggerFactory, timer))
                    //using (var h264 = new H264File(LoggerFactory))
                    {
                        var vstToPcmInput = vstBufferPool.makeEmptyBufferBlock();
                        var vstToPcmOutput = vstBufferPool.makeBufferBlock();
                        var pcmToOpusInput = pcmPool.makeEmptyBufferBlock();
                        var pcmToOpusOutput = pcmPool.makeBufferBlock();
                        var audioToFtlInput = audioPool.makeBufferBlock();
                        var audioToFtlOutput = audioPool.makeEmptyBufferBlock();
                        var bmpToVideoInput = bmpPool.makeEmptyBufferBlock();
                        var bmpToVideoOutput = bmpPool.makeBufferBlock(); 
                        var videoToFtlInput = videoPool.makeBufferBlock();
                        var videoToFtlOutput = videoPool.makeEmptyBufferBlock();

                        var effect = vst.AddEffect("Synth1 VST.dll");
                        //var effect = vst.AddEffect("Dexed.dll");

                        using (var ftl = new FtlIngest(streamKey, LoggerFactory, timer))
                        {
                            ftl.Connect();

                            var taskSet = new HashSet<Task>();
                            /*
                            taskSet.Add(effect.Interval(
                                vstToPcmOutput,
                                vstToPcmInput,
                                ct
                            ));
                            */
                            taskSet.Add(effect.Run(
                                vstToPcmInput,
                                vstToPcmOutput,
                                pcmToOpusOutput,
                                pcmToOpusInput,
                                midiEventInput,
                                ct
                            ));
                            /*
                            taskSet.Add(toPcm.Run(
                                vstToPcmInput,
                                vstToPcmOutput,
                                pcmToOpusOutput,
                                pcmToOpusInput,
                                ct
                            ));
                            */
                            taskSet.Add(opus.Run(
                                pcmToOpusInput,
                                pcmToOpusOutput,
                                audioToFtlInput,
                                audioToFtlOutput,
                                ct
                            ));
                            
                            taskSet.Add(fft.Run(
                                //vstToOpusInput,
                                //vstToOpusOutput,
                                bmpToVideoOutput,
                                bmpToVideoInput,
                                ct
                            ));

                            taskSet.Add(h264.Run(
                                bmpToVideoInput,
                                bmpToVideoOutput,
                                videoToFtlInput,
                                videoToFtlOutput,
                                ct
                            ));

                            taskSet.Add(ftl.Run(
                                audioToFtlOutput,
                                audioToFtlInput,
                                ct
                            ));

                            taskSet.Add(ftl.Run(
                                videoToFtlOutput,
                                videoToFtlInput,
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

        private async Task Loop3(CancellationTokenSource processCancel)
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    var width = 1280;
                    var height = 720;
                    var targetBitrate = 5_000_000;
                    var maxFrameRate = 30.0f;
                    var intraFrameIntervalMs = 1000;

                    var samplingRate = 48000;
                    var sampleLength = 0.06;// 0.06;
                    var blockSize = (int)(samplingRate * sampleLength);

                    var streamKey = Configuration["MIXER_STREAM_KEY"];

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

                    var audioInterval = (((double)blockSize / samplingRate) * 1_000_000.0);
                    var videoInterval = 1_000_000.0 / maxFrameRate;

                    using (var timer = new Core.Timer())
                    using (var audioWaiter = new Waiter(timer, audioInterval, ct))
                    using (var videoWaiter = new Waiter(timer, videoInterval, ct))
                    using (var vstBufferPool = new BufferPool<VstBuffer<float>>(3, () => { return new VstBuffer<float>(blockSize, 2); }))
                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(3, () => { return new PcmBuffer<float>(blockSize, 2); }))
                    using (var audioPool = new BufferPool<OpusOutputBuffer>(3, () => { return new OpusOutputBuffer(5000); }))
                    using (var bmpPool = new BufferPool<H264InputBuffer>(3, () => { return new H264InputBuffer(width, height); }))
                    using (var videoPool = new BufferPool<H264OutputBuffer>(3, () => { return new H264OutputBuffer(100000); }))
                    using (var vst = new AudioMaster<float>(samplingRate, blockSize, LoggerFactory, timer))
                    //using (var toPcm = new ToPcm<float>(LoggerFactory, timer))
                    using (var opus = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, LoggerFactory, timer))
                    using (var fft = new FFTEncoder(width, height, maxFrameRate, LoggerFactory, timer))
                    using (var h264 = new H264Encoder(width, height, targetBitrate, maxFrameRate, intraFrameIntervalMs, LoggerFactory, timer))
                    //using (var h264 = new H264File(LoggerFactory))
                    {
                        var vstToPcmOutput = vstBufferPool.makeBufferBlock();
                        var pcmToOpusOutput = pcmPool.makeBufferBlock();
                        var audioToFtlInput = audioPool.makeBufferBlock();

                        var bmpToVideoInput = bmpPool.makeEmptyBufferBlock();
                        var bmpToVideoOutput = bmpPool.makeBufferBlock();
                        var videoToFtlInput = videoPool.makeBufferBlock();
                        var videoToFtlOutput = videoPool.makeEmptyBufferBlock();

                        var effect = vst.AddEffect("Synth1 VST.dll");
                        //var effect = vst.AddEffect("Dexed.dll");

                        using (var ftl = new FtlIngest(streamKey, LoggerFactory, timer))
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
                                    new ActionBlock<H264InputBuffer>(buffer =>
                                    {
                                        buffer.Log.Clear();
                                        //var bmp = bmpToVideoOutput.Receive(ct);
                                        videoWaiter.Wait();
                                        buffer.Log.Add("[video] start", timer.USecDouble);

                                        //FFT
                                        fft.Execute(buffer);
                                        //bmpToVideoOutput.Post(bmp);

                                        //YUV

                                        //H264
                                        buffer.Log.Add("[video] start ftl input get", timer.USecDouble);
                                        var video = videoToFtlInput.Receive(ct);
                                        buffer.Log.Add("[video] end ftl input get", timer.USecDouble);
                                        h264.Execute(buffer, video, (intraFrameCount <= 0));
                                        bmpToVideoOutput.Post(buffer);
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
                                bmpToVideoOutput.LinkTo(videoWorkerBlock);
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
            processTask = Loop22(processCancel);
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

                    Int32 samplingRate = 48000;
                    Int32 blockSize = (Int32)(samplingRate * 0.05);

                    using (var vstBufferPool = new BufferPool<VstBuffer<float>>(3, () => { return new VstBuffer<float>(blockSize, 2); }))
                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(3, () => { return new PcmBuffer<float>(blockSize, 2); }))
                    {
                        var vstToPcmInput = vstBufferPool.makeEmptyBufferBlock();
                        var vstToPcmOutput = vstBufferPool.makeBufferBlock();

                        var pcmToWaveInput = pcmPool.makeEmptyBufferBlock();
                        var pcmToWaveOutput = pcmPool.makeBufferBlock();

                        using (var timer = new Core.Timer())
                        using (var vst = new AudioMaster<float>(samplingRate, blockSize, LoggerFactory, timer))
                        //using (var toPcm = new ToPcm<float>(LoggerFactory, timer))
                        using (var wave = new WaveOutFloat(
                            0,
                            2,
                            (uint)samplingRate,
                            WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT | WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT,
                            LoggerFactory,
                            timer))
                        {
                            var effect = vst.AddEffect("Synth1 VST.dll");
                            //var effect = vst.AddEffect("Dexed.dll");

                            var taskSet = new HashSet<Task>();
                            /*
                            taskSet.Add(effect.Interval(
                                vstToPcmOutput,
                                vstToPcmInput,
                                ct
                            ));
                            */
                            taskSet.Add(effect.Run(
                                vstToPcmInput,
                                vstToPcmOutput,
                                pcmToWaveInput,
                                pcmToWaveOutput,
                                midiEventInput,
                                ct
                            ));
                            /*
                            taskSet.Add(toPcm.Run(
                                vstToPcmInput,
                                vstToPcmOutput,
                                pcmToWaveInput,
                                pcmToWaveOutput,
                                ct
                            ));
                            */
                            taskSet.Add(wave.Run(
                                pcmToWaveOutput,
                                ct
                            ));

                            taskSet.Add(wave.Release(
                                pcmToWaveInput,
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

                    Int32 samplingRate = 48000;
                    Int32 blockSize = (Int32)(samplingRate * 0.05);

                    using (var vstBufferPool = new BufferPool<VstBuffer<float>>(3, () => { return new VstBuffer<float>(blockSize, 2); }))
                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(3, () => { return new PcmBuffer<float>(blockSize, 2); }))
                    {
                        var vstToPcmOutput = vstBufferPool.makeBufferBlock();
                        var pcmToWaveOutput = pcmPool.makeBufferBlock();

                        var interval = (((double)blockSize / samplingRate) * 1_000_000.0);

                        using (var timer = new Core.Timer())
                        using (var w = new Waiter(timer, interval, ct))
                        using (var vst = new AudioMaster<float>(samplingRate, blockSize, LoggerFactory, timer))
                        //using (var toPcm = new ToPcm<float>(LoggerFactory, timer))
                        using (var wave = new WaveOutFloat(
                            0,
                            2,
                            (uint)samplingRate,
                            WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT | WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT,
                            LoggerFactory,
                            timer))
                        {
                            //var effect = vst.AddEffect("Synth1 VST.dll");
                            var effect = vst.AddEffect("Dexed.dll");

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

        /*
        private async Task Loop3()
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    Int32 samplingRate = 48000;
                    Int32 blockSize = 2880;

                    double a = Math.Sin(0);
                    double b = Math.Sin(2 * 3.14159 * 440 / samplingRate);
                    double c = 2 * Math.Cos(2 * 3.14159 * 440 / samplingRate);

                    using (var pcm1 = new PcmBuffer<float>(blockSize, 2))
                    {
                        var vstToOpusInput = new BufferBlock<PcmBuffer<float>>();

                        var count = blockSize;
                        var idx = 0;
                        for (int i = 0; i < count; i++)
                        {
                            var d = a;
                            a = b;
                            b = c * a - d;
                            
                            pcm1.Target[idx++] = (float)d;
                            pcm1.Target[idx++] = (float)(d/2);
                        }

                        vstToOpusInput.Post(pcm1);

                        using (var wave = new WaveOutFloat(
                            0,
                            2,
                            (uint)samplingRate,
                            Wave.WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT | Wave.WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT, 
                            LoggerFactory))
                        {
                            var taskSet = new HashSet<Task>();

                            taskSet.Add(wave.Run(
                                vstToOpusInput,
                                ct
                            ));

                            taskSet.Add(wave.Release(
                                vstToOpusInput,
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

        private async Task Loop4()
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    using (var videoPool = new BufferPool<H264OutputBuffer>(3, () => { return new H264OutputBuffer(10000000); }))
                    {
                        var videoToFtlInput = videoPool.makeBufferBlock();

                        using (var timer = new Momiji.Core.Timer())
                        using (var h264 = new H264Encoder(100, 100, 5_000_000, 30.0f, 1000, LoggerFactory, timer))
                        {
                        }
                    }
                });
            }
            finally
            {
                Logger.LogInformation("main loop end");
            }
        }

        private async Task Loop5()
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    Int32 samplingRate = 48000;
                    Int32 blockSize = (Int32)(samplingRate * 0.05);

                    using (var pcm1 = new PcmBuffer<float>(blockSize, 2))
                    using (var pcm2 = new PcmBuffer<float>(blockSize, 2))
                    using (var out1 = new OpusOutputBuffer(5000))
                    using (var out2 = new OpusOutputBuffer(5000))
                    {
                        var vstToOpusInput = new BufferBlock<PcmBuffer<float>>();
                        var vstToOpusOutput = new BufferBlock<PcmBuffer<float>>();
                        var opusToFtlInput = new BufferBlock<OpusOutputBuffer>();

                        vstToOpusOutput.Post(pcm1);
                        vstToOpusOutput.Post(pcm2);
                        opusToFtlInput.Post(out1);
                        opusToFtlInput.Post(out2);

                        using (var timer = new Momiji.Core.Timer())
                        using (var vst = new AudioMaster<float>(samplingRate, blockSize, LoggerFactory, timer))
                        using (var encoder = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, LoggerFactory))
                        {
                            var effect = vst.AddEffect("Synth1 VST.dll");

                            var taskSet = new HashSet<Task>();

                            taskSet.Add(effect.Run(
                                vstToOpusOutput,
                                vstToOpusInput,
                                midiEventInput,
                                ct
                            ));

                            taskSet.Add(encoder.Run(
                                vstToOpusInput,
                                vstToOpusOutput,
                                opusToFtlInput,
                                opusToFtlInput,
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
        */

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
                midiEventInput.Post(vstEvent);
            }
        }
    }
}