﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Ftl;
using Momiji.Core.Opus;
using Momiji.Core.Vst;
using Momiji.Core.Wave;
using Momiji.Interop;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class HomeController : Controller
    {
        private static CancellationTokenSource processCancel;
        private static Task processTask;
        private IConfiguration Configuration { get; }
        private ILogger Logger { get; }

        public HomeController(IConfiguration configuration, ILogger<HomeController> logger)
        {
            Configuration = configuration;
            Logger = logger;
        }

        private async Task Loop()
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    Int32 samplingRate = 48000;
                    Int32 blockSize = (Int32)(samplingRate * 0.06);
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

                    using (var pcm1 = new PcmBuffer<float>(blockSize, 2))
                    using (var pcm2 = new PcmBuffer<float>(blockSize, 2))
                    using (var pcm3 = new PcmBuffer<float>(blockSize, 2))
                    using (var pcm4 = new PcmBuffer<float>(blockSize, 2))
                    using (var out1 = new OpusOutputBuffer(5000))
                    using (var out2 = new OpusOutputBuffer(5000))
                    using (var out3 = new OpusOutputBuffer(5000))
                    using (var out4 = new OpusOutputBuffer(5000))
                    using (var video1 = new PinnedBuffer<byte[]>(new byte[blockSize * 2]))
                    using (var video2 = new PinnedBuffer<byte[]>(new byte[blockSize * 2]))
                    {
                        var vstToOpusInput = new BufferBlock<PcmBuffer<float>>();
                        var vstToOpusOutput = new BufferBlock<PcmBuffer<float>>();
                        var opusToFtlInput = new BufferBlock<OpusOutputBuffer>();
                        var opusToFtlOutput = new BufferBlock<OpusOutputBuffer>();
                        var videoToFtlInput = new BufferBlock<PinnedBuffer<byte[]>>();
                        var videoToFtlOutput = new BufferBlock<PinnedBuffer<byte[]>>();
                        var midiEventInput = new BufferBlock<Vst.VstMidiEvent>();

                        vstToOpusOutput.Post(pcm1);
                        vstToOpusOutput.Post(pcm2);
                        vstToOpusOutput.Post(pcm3);
                        vstToOpusOutput.Post(pcm4);
                        opusToFtlInput.Post(out1);
                        opusToFtlInput.Post(out2);
                        opusToFtlInput.Post(out3);
                        opusToFtlInput.Post(out4);
                        videoToFtlInput.Post(video1);
                        videoToFtlInput.Post(video2);

                        using (var vst = new AudioMaster<float>(samplingRate, blockSize))
                        using (var encoder = new OpusEncoder(Opus.SamplingRate.Sampling48000, Opus.Channels.Stereo))
                        using (var ftl = new FtlIngest($"{Configuration["MIXER_STREAM_KEY"]}"))
                        {
                            var effect = vst.AddEffect("Synth1 VST.dll");

                            effect.Run(
                                vstToOpusOutput,
                                vstToOpusInput,
                                midiEventInput,
                                ct
                            );
                            
                            encoder.Run(
                                vstToOpusInput,
                                vstToOpusOutput,
                                opusToFtlInput,
                                opusToFtlOutput,
                                ct
                            );
                            
                            ftl.Run(
                                opusToFtlOutput,
                                opusToFtlInput,
                                videoToFtlInput,
                                videoToFtlInput,
                                ct
                            );

                            int a = 0;
                            bool on = true;
                            byte note = 0x40;
                            byte v = 0x40;

                            while (true)
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    break;
                                }
                                Logger.LogInformation("wait:" + a++);

                                var vstEvent = new Vst.VstMidiEvent();
                                vstEvent.type = Vst.VstEvent.VstEventTypes.kVstMidiType;
                                vstEvent.byteSize = Marshal.SizeOf<Vst.VstMidiEvent>();
                                vstEvent.deltaFrames = 0;
                                vstEvent.flags = Vst.VstMidiEvent.VstMidiEventFlags.kVstMidiEventIsRealtime;

                                if (on)
                                {
                                    vstEvent.midiData0 = 0x90;
                                    vstEvent.midiData1 = note;
                                    vstEvent.midiData2 = v;
                                    vstEvent.midiData3 = 0x00;
                                }
                                else
                                {
                                    vstEvent.midiData0 = 0x80;
                                    vstEvent.midiData1 = note;
                                    vstEvent.midiData2 = 0x00;
                                    vstEvent.midiData3 = 0x00;

                                    note++;
                                    if (note < 0x40)
                                    {
                                        note = 0x40;
                                    }

                                    v++;
                                }
                                on = !on;

                                midiEventInput.Post(vstEvent);

                                Thread.Sleep(1000);
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


        private async Task Loop2()
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    Int32 samplingRate = 48000;
                    Int32 blockSize = (Int32)(samplingRate * 0.05/*0.05*/);

                    using (var pcm1 = new PcmBuffer<float>(blockSize, 2))
                    using (var pcm2 = new PcmBuffer<float>(blockSize, 2))
                    using (var pcm3 = new PcmBuffer<float>(blockSize, 2))
                    using (var pcm4 = new PcmBuffer<float>(blockSize, 2))
                    using (var pcm5 = new PcmBuffer<float>(blockSize, 2))
                    using (var pcm6 = new PcmBuffer<float>(blockSize, 2))
                    {
                        var vstToOpusInput = new BufferBlock<PcmBuffer<float>>();
                        var vstToOpusOutput = new BufferBlock<PcmBuffer<float>>();
                        var midiEventInput = new BufferBlock<Vst.VstMidiEvent>();

                        vstToOpusInput.Post(pcm1);
                        vstToOpusInput.Post(pcm2);
                        //vstToOpusInput.Post(pcm3);

                        //vstToOpusOutput.Post(pcm4);
                        //vstToOpusOutput.Post(pcm5);
                        //vstToOpusOutput.Post(pcm6);

                        using (var vst = new AudioMaster<float>(samplingRate, blockSize))
                        using (var wave = new Momiji.Core.Wave.WaveOut<float>(
                        //using (var wave = new Momiji.Test.WaveFile.WaveFile(
                            0,
                            2,
                            (uint)samplingRate,
                            (ushort)(Marshal.SizeOf<float>() * 8),
                            //(ushort)(Marshal.SizeOf<short>() * 8),
                            Wave.WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT | Wave.WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT,
                            new Guid("00000003-0000-0010-8000-00aa00389b71"),
                            //new Guid("00000001-0000-0010-8000-00aa00389b71"),
                            (uint)blockSize))
                        {
                            var effect = vst.AddEffect("Synth1 VST.dll");

                            effect.Run(
                                vstToOpusOutput,
                                vstToOpusInput,
                                midiEventInput,
                                ct
                            );

                            wave.Run(
                                vstToOpusInput,
                                vstToOpusOutput,
                                ct
                            );

                            int a = 0;
                            bool on = true;
                            byte note = 0x40;
                            byte v = 0x40;

                            while (true)
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    break;
                                }
                                Logger.LogInformation("wait:{a}", a);

                                var vstEvent = new Vst.VstMidiEvent();
                                vstEvent.type = Vst.VstEvent.VstEventTypes.kVstMidiType;
                                vstEvent.byteSize = Marshal.SizeOf<Vst.VstMidiEvent>();
                                vstEvent.deltaFrames = 0;
                                vstEvent.flags = Vst.VstMidiEvent.VstMidiEventFlags.kVstMidiEventIsRealtime;

                                if (on)
                                {
                                    vstEvent.midiData0 = 0x90;
                                    vstEvent.midiData1 = note;
                                    vstEvent.midiData2 = v;
                                    vstEvent.midiData3 = 0x00;
                                }
                                else
                                {
                                    vstEvent.midiData0 = 0x80;
                                    vstEvent.midiData1 = note;
                                    vstEvent.midiData2 = 0x00;
                                    vstEvent.midiData3 = 0x00;

                                    note++;
                                    v++;
                                }
                                on = !on;

                                midiEventInput.Post(vstEvent);

                                Thread.Sleep(1000);
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

                        using (var wave = new Momiji.Core.Wave.WaveOut<float>(
                            0,
                            2,
                            (uint)samplingRate,
                            (ushort)(Marshal.SizeOf<float>() * 8),
                            Wave.WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT | Wave.WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT,
                            new Guid("00000003-0000-0010-8000-00aa00389b71"),
                            (uint)blockSize))
                        {

                            wave.Run(
                                vstToOpusInput,
                                vstToOpusInput,
                                ct
                            );

                            while (true)
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    break;
                                }
                                Logger.LogInformation("wait");
                                Thread.Sleep(1000);
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
                    Int32 blockSize = (Int32)(samplingRate * 0.05/*0.05*/);

                    using (var pcm1 = new PcmBuffer<float>(blockSize, 2))
                    using (var pcm2 = new PcmBuffer<float>(blockSize, 2))
                    using (var out1 = new OpusOutputBuffer(5000))
                    using (var out2 = new OpusOutputBuffer(5000))
                    {
                        var vstToOpusInput = new BufferBlock<PcmBuffer<float>>();
                        var vstToOpusOutput = new BufferBlock<PcmBuffer<float>>();
                        var opusToFtlInput = new BufferBlock<OpusOutputBuffer>();
                        var midiEventInput = new BufferBlock<Vst.VstMidiEvent>();

                        vstToOpusOutput.Post(pcm1);
                        vstToOpusOutput.Post(pcm2);
                        opusToFtlInput.Post(out1);
                        opusToFtlInput.Post(out2);

                        using (var vst = new AudioMaster<float>(samplingRate, blockSize))
                        using (var encoder = new OpusEncoder(Opus.SamplingRate.Sampling48000, Opus.Channels.Stereo))
                        {
                            var effect = vst.AddEffect("Synth1 VST.dll");

                            effect.Run(
                                vstToOpusOutput,
                                vstToOpusInput,
                                midiEventInput,
                                ct
                            );

                            encoder.Run(
                                vstToOpusInput,
                                vstToOpusOutput,
                                opusToFtlInput,
                                opusToFtlInput,
                                ct
                            );


                            int a = 0;
                            bool on = true;
                            byte note = 0x40;
                            byte v = 0x40;

                            while (true)
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    break;
                                }
                                Logger.LogInformation("wait:" + a++);

                                var vstEvent = new Vst.VstMidiEvent();
                                vstEvent.type = Vst.VstEvent.VstEventTypes.kVstMidiType;
                                vstEvent.byteSize = Marshal.SizeOf<Vst.VstMidiEvent>();
                                vstEvent.deltaFrames = 0;
                                vstEvent.flags = Vst.VstMidiEvent.VstMidiEventFlags.kVstMidiEventIsRealtime;

                                if (on)
                                {
                                    vstEvent.midiData0 = 0x90;
                                    vstEvent.midiData1 = note;
                                    vstEvent.midiData2 = v;
                                    vstEvent.midiData3 = 0x00;
                                }
                                else
                                {
                                    vstEvent.midiData0 = 0x80;
                                    vstEvent.midiData1 = note;
                                    vstEvent.midiData2 = 0x00;
                                    vstEvent.midiData3 = 0x00;

                                    note++;
                                    v++;
                                }
                                on = !on;

                                midiEventInput.Post(vstEvent);

                                Thread.Sleep(1000);
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

        public IActionResult Start()
        {
            ViewData["Message"] = "Start.";
            if (processCancel == null)
            {
                processCancel = new CancellationTokenSource();
                processTask = Loop();
            }

            return View();
        }

        public IActionResult Start2()
        {
            ViewData["Message"] = "Start.";
            if (processCancel == null)
            {
                processCancel = new CancellationTokenSource();
                processTask = Loop2();
            }

            return View();
        }

        public IActionResult Start3()
        {
            ViewData["Message"] = "Start.";
            if (processCancel == null)
            {
                processCancel = new CancellationTokenSource();
                processTask = Loop3();
            }

            return View();
        }

        public IActionResult Start5()
        {
            ViewData["Message"] = "Start.";
            if (processCancel == null)
            {
                processCancel = new CancellationTokenSource();
                processTask = Loop5();
            }

            return View();
        }

        public IActionResult Stop()
        {
            ViewData["Message"] = "Stop.";
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
                        Logger.LogInformation("FtlIngest Process Exception:" + e.Message + " " + v.Message);
                    }
                }
                finally
                {
                    processCancel.Dispose();
                    processCancel = null;
                }
            }
            return View();
        }
        
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult WaveCaps()
        {
            var n = Momiji.Core.Wave.WaveOut<float>.GetNumDevices();
            for (uint i = 0; i < n; i++)
            {
                var c = Momiji.Core.Wave.WaveOut<float>.GetCapabilities(i);
                Logger.LogInformation($"{c}");
            }

            return View();
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}