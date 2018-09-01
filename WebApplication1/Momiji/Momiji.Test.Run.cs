using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core;
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
        Task Loop(CancellationTokenSource processCancel);
        Task Loop2(CancellationTokenSource processCancel);
        void Note(MIDIMessageEvent[] midiMessage);
    }

    public class Runner : IRunner
    {
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        private static BufferBlock<VstMidiEvent> midiEventInput = new BufferBlock<VstMidiEvent>();

        public Runner(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            Configuration = configuration;
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Runner>();
        }

        public async Task Loop(CancellationTokenSource processCancel)
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

                    using (var timer = new Momiji.Core.Timer())
                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(2, () => { return new PcmBuffer<float>(blockSize, 2); }))
                    using (var audioPool = new BufferPool<OpusOutputBuffer>(2, () => { return new OpusOutputBuffer(5000); }))
                    using (var bmpPool = new BufferPool<H264InputBuffer>(2, () => { return new H264InputBuffer(width, height); }))
                    using (var videoPool = new BufferPool<H264OutputBuffer>(2, () => { return new H264OutputBuffer(100000); }))
                    using (var vst = new AudioMaster<float>(samplingRate, blockSize, LoggerFactory, timer))
                    using (var opus = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, LoggerFactory))
                    using (var h264 = new H264Encoder(width, height, targetBitrate, maxFrameRate, intraFrameIntervalMs, LoggerFactory, timer))
                    //using (var h264 = new H264File(LoggerFactory))
                    {
                        var vstToOpusInput = pcmPool.makeEmptyBufferBlock();
                        var vstToOpusOutput = pcmPool.makeBufferBlock();
                        var audioToFtlInput = audioPool.makeBufferBlock();
                        var audioToFtlOutput = audioPool.makeEmptyBufferBlock();
                        var bmpToVideoInput = bmpPool.makeBufferBlock();
                        var bmpToVideoOutput = bmpPool.makeEmptyBufferBlock();
                        var videoToFtlInput = videoPool.makeBufferBlock();
                        var videoToFtlOutput = videoPool.makeEmptyBufferBlock();

                        var effect = vst.AddEffect("Synth1 VST.dll");
                        //var effect = vst.AddEffect("Dexed.dll");

                        using (var ftl = new FtlIngest(streamKey, LoggerFactory, timer))
                        {
                            var taskSet = new HashSet<Task>();

                            taskSet.Add(effect.Run(
                                vstToOpusOutput,
                                vstToOpusInput,
                                midiEventInput,
                                ct
                            ));

                            taskSet.Add(opus.Run(
                                vstToOpusInput,
                                vstToOpusOutput,
                                audioToFtlInput,
                                audioToFtlOutput,
                                ct
                            ));

                            taskSet.Add(h264.Run(
                                bmpToVideoInput,
                                bmpToVideoInput,
                                //bmpToVideoOutput,
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

        public async Task Loop2(CancellationTokenSource processCancel)
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

                    using (var pcmPool = new BufferPool<PcmBuffer<float>>(2, () => { return new PcmBuffer<float>(blockSize, 2); }))
                    {
                        var vstToOpusInput = pcmPool.makeBufferBlock();
                        var vstToOpusOutput = new BufferBlock<PcmBuffer<float>>();

                        using (var timer = new Momiji.Core.Timer())
                        using (var vst = new AudioMaster<float>(samplingRate, blockSize, LoggerFactory, timer))
                        using (var wave = new WaveOutFloat(
                            0,
                            2,
                            (uint)samplingRate,
                            WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT | WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT,
                            LoggerFactory))
                        {
                            //var effect = vst.AddEffect("Synth1 VST.dll");
                            var effect = vst.AddEffect("Dexed.dll");

                            var taskSet = new HashSet<Task>();

                            taskSet.Add(effect.Run(
                                vstToOpusOutput,
                                vstToOpusInput,
                                midiEventInput,
                                ct
                            ));

                            taskSet.Add(wave.Run(
                                vstToOpusInput,
                                ct
                            ));

                            taskSet.Add(wave.Release(
                                vstToOpusOutput,
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