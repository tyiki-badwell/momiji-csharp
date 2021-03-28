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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace mixerTest
{
    public interface IRunner
    {
        bool Start();
        bool Cancel();

        //void Note(MIDIMessageEvent[] midiMessage);
        Task Play(WebSocket webSocket);
    }

    public class Param
    {
        public int BufferCount { get; set; }
        public bool Local { get; set; }
        public bool Connect { get; set; }

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

    public class Runner : IRunner, IDisposable
    {
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private IDllManager DllManager { get; }
        private string StreamKey { get; }
        private string IngestHostname { get; }
        private string CaInfoPath { get; }
        private Param Param { get; }

        private bool disposed;
        private CancellationTokenSource processCancel;
        private Task processTask;
        private readonly BufferBlock<MIDIMessageEvent2> midiEventInput = new();
        private readonly BufferBlock<MIDIMessageEvent2> midiEventOutput = new();

        private readonly IDictionary<WebSocket, int> webSocketPool = new ConcurrentDictionary<WebSocket, int>();

        //private BufferBlock<OpusOutputBuffer> audioOutput = new BufferBlock<OpusOutputBuffer>();
        //private BufferBlock<H264OutputBuffer> videoOutput = new BufferBlock<H264OutputBuffer>();
        
        public Runner(IConfiguration configuration, ILoggerFactory loggerFactory, IDllManager dllManager)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Runner>();
            DllManager = dllManager;

            var param = new Param();
            Configuration.GetSection("Param").Bind(param);
            Param = param;

            StreamKey = Configuration["MIXER_STREAM_KEY"];
            IngestHostname = Configuration["MIXER_INGEST_HOSTNAME"];

            CaInfoPath =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "lib",
                    "cacert.pem"
                );
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;

            if (disposing)
            {
                Cancel();
            }
            disposed = true;
        }

        public bool Start()
        {
            if (processCancel != null)
            {
                Logger.LogInformation("[home] already started.");
                return false;
            }
            processCancel = new CancellationTokenSource();
            if (Param.Local)
            {
                processTask = Loop2();
            }
            else
            {
                processTask = Loop3();
            }

            Logger.LogInformation("[home] started.");
            return true;
        }


        public bool Cancel()
        {
            if (processCancel == null)
            {
                Logger.LogInformation("[home] already stopped.");
                return false;
            }

            try
            {
                try
                {
                    processCancel.Cancel();
                }
                catch (AggregateException e)
                {
                    Logger.LogInformation(e, "[home] Process Cancel Exception");
                }

                try
                {
                    processTask.Wait();
                }
                catch (AggregateException e)
                {
                    Logger.LogInformation(e, "[home] Process Task Exception");
                }
            }
            finally
            {
                processTask.Dispose();
                processTask = null;
                processCancel.Dispose();
                processCancel = null;
            }
            Logger.LogInformation("[home] stopped.");
            return true;
        }

        private async Task Loop3()
        {
            var ct = processCancel.Token;

            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    var blockSize = (int)(Param.SamplingRate * Param.SampleLength);
                    
                    var audioInterval = 1_000_000.0 * Param.SampleLength;
                    var videoInterval = 1_000_000.0 / Param.MaxFrameRate;

                    using var timer = new Momiji.Core.Timer();
                    using var audioWaiter = new Waiter(timer, audioInterval, ct);
                    using var videoWaiter = new Waiter(timer, videoInterval, ct);
                    using var vstBufferPool = new BufferPool<VstBuffer<float>>(Param.BufferCount, () => new VstBuffer<float>(blockSize, 2), LoggerFactory);
                    using var pcmPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => new PcmBuffer<float>(blockSize, 2), LoggerFactory);
                    using var audioPool = new BufferPool<OpusOutputBuffer>(Param.BufferCount, () => new OpusOutputBuffer(5000), LoggerFactory);
                    using var pcmDummyPool = new BufferPool<PcmBuffer<float>>(Param.BufferCount, () => new PcmBuffer<float>(blockSize, 2), LoggerFactory);
                    using var bmpPool = new BufferPool<H264InputBuffer>(Param.BufferCount, () => new H264InputBuffer(Param.Width, Param.Height), LoggerFactory);
                    using var videoPool = new BufferPool<H264OutputBuffer>(Param.BufferCount, () => new H264OutputBuffer(200000), LoggerFactory);
                    using var vst = new AudioMaster<float>(Param.SamplingRate, blockSize, LoggerFactory, timer, DllManager);
                    using var toPcm = new ToPcm<float>(LoggerFactory, timer);
                    using var opus = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, LoggerFactory, timer);
                    using var fft = new FFTEncoder(Param.Width, Param.Height, Param.MaxFrameRate, LoggerFactory, timer);
                    using var h264 = new H264Encoder(Param.Width, Param.Height, Param.TargetBitrate, Param.MaxFrameRate, LoggerFactory, timer);
                    var effect = vst.AddEffect(Param.EffectName);

                    using var ftl = new FtlIngest(StreamKey, IngestHostname, LoggerFactory, timer, Param.Connect, default, CaInfoPath);
                    ftl.Connect();

                    var taskSet = new HashSet<Task>();
                    var options = new ExecutionDataflowBlockOptions
                    {
                        CancellationToken = ct,
                        MaxDegreeOfParallelism = 1
                    };

                    {
                        var vstBlock =
                            new TransformBlock<VstBuffer<float>, PcmBuffer<float>>(buffer =>
                            {
                                buffer.Log.Clear();
                                audioWaiter.Wait();
                                buffer.Log.Add("[audio] start", timer.USecDouble);

                                //VST
                                var nowTime = timer.USecDouble;
                                effect.ProcessEvent(nowTime, midiEventInput);
                                effect.ProcessReplacing(nowTime, buffer);

                                var pcmTask = pcmPool.ReceiveAsync(ct);
                                //trans
                                var pcm = toPcm.Execute(buffer, pcmTask);
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
                                pcmPool.SendAsync(buffer);
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
                        midiEventOutput.LinkTo(midiDataStoreBlock);

                        var fftBlock =
                            new TransformBlock<PcmBuffer<float>, H264InputBuffer>(buffer =>
                            {
                                buffer.Log.Clear();

                                videoWaiter.Wait();
                                buffer.Log.Add("[video] start", timer.USecDouble);

                                //FFT
                                var bmpTask = bmpPool.ReceiveAsync(ct);
                                fft.Execute(buffer, bmpTask);
                                pcmDummyPool.SendAsync(buffer);
                                return bmpTask;
                            }, options);
                        taskSet.Add(fftBlock.Completion);
                        pcmDummyPool.LinkTo(fftBlock);

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
                        any.ConfigureAwait(false);
                        any.Wait();
                        var task = any.Result;
                        taskSet.Remove(task);
                        if (task.IsFaulted)
                        {
                            processCancel.Cancel();
                            Logger.LogError(task.Exception, "Process Exception");
                        }
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.LogInformation(e, "Exception");
                throw;
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
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

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

                    var taskSet = new HashSet<Task>();
                    var options = new ExecutionDataflowBlockOptions
                    {
                        CancellationToken = ct,
                        MaxDegreeOfParallelism = 1
                    };

                    var vstBlock =
                        new TransformBlock<VstBuffer<float>, PcmBuffer<float>>(buffer =>
                        {
                            buffer.Log.Clear();
                            audioWaiter.Wait();
                            //VST
                            var nowTime = timer.USecDouble;
                            effect.ProcessEvent(nowTime, midiEventInput);
                            effect.ProcessReplacing(nowTime, buffer);

                            var pcmTask = pcmPool.ReceiveAsync(ct);
                            //trans
                            var pcm = toPcm.Execute(buffer, pcmTask);
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
                        any.ConfigureAwait(false);
                        any.Wait();
                        var task = any.Result;
                        taskSet.Remove(task);
                        if (task.IsFaulted)
                        {
                            processCancel.Cancel();
                            Logger.LogError(task.Exception, "Process Exception");
                        }
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.LogInformation(e, "Exception");
                throw;
            }
            finally
            {
                Logger.LogInformation("main loop end");
            }
        }

        /*
        public void Note(MIDIMessageEvent[] midiMessage)
        {
            List<MIDIMessageEvent> list = new List<MIDIMessageEvent>(midiMessage);
            list.Sort((a, b) => (int)(a.receivedTime - b.receivedTime));
            
            foreach (var midiEvent in list)
            {
                Logger.LogInformation(
                    $"note {DateTimeOffset.FromUnixTimeMilliseconds((long)midiEvent.receivedTime).ToUniversalTime():HH:mm:ss.fff} => " +
                    $"{midiEvent.data0:X2}" +
                    $"{midiEvent.data1:X2}" +
                    $"{midiEvent.data2:X2}" +
                    $"{midiEvent.data3:X2}"
                );
                midiEventInput.SendAsync(midiEvent);
            }
        }
        */
        public async Task Play(WebSocket webSocket)
        {
            if (processCancel == null)
            {
                return;
            }
            if (webSocket == default)
            {
                throw new ArgumentNullException(nameof(webSocket));
            }

            Logger.LogInformation("websocket start");

            var ct = processCancel.Token;

            try
            {
                await Task.Run(() =>
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    var buf = new byte[1024];

                    using var timer = new Momiji.Core.Timer();
                    while (webSocket.State == WebSocketState.Open)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            break;
                        }
                        //Logger.LogInformation("websocket try receive");
                        var task = webSocket.ReceiveAsync(buf, ct);
                        task.Wait(ct);
                        var result = task.Result;
                        if (result.CloseStatus.HasValue)
                        {
                            webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, ct);
                            break;
                        }

                        MIDIMessageEvent midiEvent;
                        unsafe
                        {
                            var s = new Span<byte>(buf);
                            fixed (byte* p = &s.GetPinnableReference())
                            {
                                midiEvent = *(MIDIMessageEvent*)p;
                            }
                        }
                        /*
                        Logger.LogInformation(
                            $"note {DateTimeOffset.FromUnixTimeMilliseconds((long)(timer.USecDouble / 1000)).ToUniversalTime():HH:mm:ss.fff} {DateTimeOffset.FromUnixTimeMilliseconds((long)midiEvent.receivedTime).ToUniversalTime():HH:mm:ss.fff} => " +
                            $"{midiEvent.data0:X2}" +
                            $"{midiEvent.data1:X2}" +
                            $"{midiEvent.data2:X2}" +
                            $"{midiEvent.data3:X2}"
                        );*/
                        MIDIMessageEvent2 midiEvent2;
                        midiEvent2.midiMessageEvent = midiEvent;
                        midiEvent2.receivedTimeUSec = timer.USecDouble;
                        midiEventInput.SendAsync(midiEvent2);
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.LogInformation(e, "Exception");
                throw;
            }
            finally
            {
                webSocket.Dispose();
                webSocket = null;

                Logger.LogInformation("websocket end");
            }
        }
    }
}