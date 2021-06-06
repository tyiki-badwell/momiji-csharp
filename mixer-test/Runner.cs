using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.MixedReality.WebRTC;
using Momiji.Core;
using Momiji.Core.WebMidi;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
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
        Task AcceptWebSocket(WebSocket webSocket);
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
        private Param Param { get; }

        private bool disposed;
        private CancellationTokenSource processCancel;
        private Task processTask;
        private readonly BufferBlock<MIDIMessageEvent2> midiEventInput = new();
        private readonly BufferBlock<MIDIMessageEvent2> midiEventOutput = new();

        //private readonly IDictionary<WebSocket, int> webSocketPool = new ConcurrentDictionary<WebSocket, int>();

        private BroadcastBlock<string> wsBroadcaster = new(null);
        private CancellationTokenSource wsProcessCancel = new();

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

            //webSocketTask = WebSocketLoop();
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
                try
                {
                    wsProcessCancel.Cancel();
                }
                catch (AggregateException e)
                {
                    Logger.LogInformation(e, "[home] WebSocket Process Cancel Exception");
                }
                wsProcessCancel.Dispose();
                wsProcessCancel = null;

                Cancel();
            }
            disposed = true;
        }

        public bool Start()
        {
            //TODO make thread safe

            if (processCancel != null)
            {
                Logger.LogInformation("[home] already started.");
                return false;
            }
            processCancel = new CancellationTokenSource();
            processTask = Loop();

            Logger.LogInformation("[home] started.");
            return true;
        }


        public bool Cancel()
        {
            //TODO make thread safe

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
        private async Task Loop()
        {
            var ct = processCancel.Token;

            wsBroadcaster.Post("start");
            Logger.LogInformation("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    var taskSet = new HashSet<Task>();

                    new Logic2(Configuration, LoggerFactory, DllManager, Param, midiEventInput, midiEventOutput, taskSet, ct).Run();
                    //new Logic4(Configuration, LoggerFactory, DllManager, Param, midiEventInput, midiEventOutput, taskSet, ct).Run();

                    wsBroadcaster.Post("run");

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
                wsBroadcaster.Post("end");
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

        private async Task<PeerConnection> SetupPeerConnection(WebSocket webSocket, CancellationToken ct)
        {
            var pc = new PeerConnection();
            var config = new PeerConnectionConfiguration
            {
                IceServers = new List<IceServer>
                {
                    //new IceServer{ Urls = { "stun:stun.l.google.com:19302" } }
                }
            };
            await pc.InitializeAsync(config, ct).ConfigureAwait(false);
            pc.LocalSdpReadytoSend += (message) =>
            {
                Logger.LogInformation($"[peer connection] LocalSdpReadytoSend {message}");

                var param = new Dictionary<string, string>
                {
                    { "type", "offer" },
                    { "sdp", message.Content }
                };

                var json = JsonSerializer.Serialize(param);

                var buf = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));
                webSocket.SendAsync(buf, WebSocketMessageType.Text, true, ct);
            };
            pc.DataChannelRemoved += (channel) =>
            {
                Logger.LogInformation($"[peer connection] DataChannelRemoved {channel}");
            };

            pc.DataChannelAdded += (channel) =>
            {
                Logger.LogInformation($"[peer connection] DataChannelAdded {channel}");
            };

            pc.IceGatheringStateChanged += (newState) =>
            {
                Logger.LogInformation($"[peer connection] IceGatheringStateChanged {newState}");
            };

            pc.RenegotiationNeeded += () =>
            {
                Logger.LogInformation($"[peer connection] RenegotiationNeeded");
            };

            pc.TransceiverAdded += (transceiver) =>
            {
                Logger.LogInformation($"[peer connection] TransceiverAdded {transceiver}");
            };

            pc.AudioTrackAdded += (track) =>
            {
                Logger.LogInformation($"[peer connection] AudioTrackAdded {track}");
            };

            pc.AudioTrackRemoved += (transceiver, track) =>
            {
                Logger.LogInformation($"[peer connection] TransceiverAdded {transceiver} {track}");
            };

            pc.VideoTrackAdded += (track) =>
            {
                Logger.LogInformation($"[peer connection] AudioTrackAdded {track}");
            };

            pc.VideoTrackRemoved += (transceiver, track) =>
            {
                Logger.LogInformation($"[peer connection] TransceiverAdded {transceiver} {track}");
            };

            pc.IceCandidateReadytoSend += (candidate) =>
            {
                Logger.LogInformation($"[peer connection] IceCandidateReadytoSend {candidate}");
            };

            pc.Connected += () => {
                Logger.LogInformation($"[peer connection] Connected");
            };

            pc.IceStateChanged += (newState) => {
                Logger.LogInformation($"[peer connection] IceStateChanged {newState}");
            };

            //todo dispose
            var exVideoTrackSource = ExternalVideoTrackSource.CreateFromI420ACallback(I420AVideoFrameRequestDelegate);
            

            var videoTransceiver = pc.AddTransceiver(MediaKind.Video);
            
            videoTransceiver.LocalVideoTrack = LocalVideoTrack.CreateFromSource(exVideoTrackSource, new LocalVideoTrackInitConfig() { trackName = "video track" });
            videoTransceiver.DesiredDirection = Transceiver.Direction.SendOnly;

//            LocalAudioTrack localAudioTrack = new LocalAudioTrack();
            var audioTransceiver = pc.AddTransceiver(MediaKind.Audio);
//            audioTransceiver.LocalAudioTrack = localAudioTrack;
            audioTransceiver.DesiredDirection = Transceiver.Direction.SendOnly;

            pc.CreateOffer();

            return pc;
        }

        private void I420AVideoFrameRequestDelegate(in FrameRequest request)
        {
            Logger.LogInformation($"[peer connection] I420AVideoFrameRequestDelegate {request.RequestId} {request.TimestampMs}");
            request.CompleteRequest(new I420AVideoFrame() { });
        }


        public async Task AcceptWebSocket(WebSocket webSocket)
        {
            if (webSocket == default)
            {
                throw new ArgumentNullException(nameof(webSocket));
            }

            Logger.LogInformation("[web socket] start");

            var ct = wsProcessCancel.Token;

            var actionBlock = new ActionBlock<string>(message => {
                var bytes = Encoding.UTF8.GetBytes(message);
                webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            });
            wsBroadcaster.LinkTo(actionBlock);

            try
            {
                using var pc = await SetupPeerConnection(webSocket, ct).ConfigureAwait(false);

                using var timer = new Momiji.Core.Timer();
                var buf = WebSocket.CreateServerBuffer(1024);
                while (webSocket.State == WebSocketState.Open)
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    var result = await webSocket.ReceiveAsync(buf, ct).ConfigureAwait(false);
                    if (result.CloseStatus.HasValue)
                    {
                        await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, ct).ConfigureAwait(false);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        MIDIMessageEvent midiEvent = ToMIDIMessageEvent(buf.Array);
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
                        await midiEventInput.SendAsync(midiEvent2).ConfigureAwait(false);
                        await midiEventOutput.SendAsync(midiEvent2).ConfigureAwait(false);
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var text = Encoding.UTF8.GetString(buf.Array, 0, result.Count).Trim();
                        Logger.LogInformation($"[web socket] text [{text}]");

                        var param = JsonSerializer.Deserialize<Dictionary<string, string>>(text);
                        var type = param["type"];

                        if (type == "start")
                        {
                            Logger.LogInformation($"[web socket] type = {type}.");
                            Start();
                        }
                        else if (type == "cancel")
                        {
                            Logger.LogInformation($"[web socket] type = {type}.");
                            Cancel();
                        }
                        else if (type == "close")
                        {
                            Logger.LogInformation($"[web socket] type = {type}.");
                            pc.Transceivers.ForEach((t) => {
                                t.LocalTrack.Dispose();
                            });

                            pc.Close();
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "close request", ct).ConfigureAwait(false);
                            break;
                        }
                        else if (type == "offer")
                        {
                            Logger.LogInformation($"[web socket] type = {type}.");

                            var sdp = param["sdp"];
                            Logger.LogInformation($"[web socket] sdp = {sdp}");
                        }
                        else if (type == "answer")
                        {
                            Logger.LogInformation($"[web socket] type = {type}.");

                            var sdp = param["sdp"];
                            Logger.LogInformation($"[web socket] sdp = {sdp}");
                            var sdpMessage = new SdpMessage()
                            {
                                Type = SdpMessageType.Answer,
                                Content = sdp
                            };

                            await pc.SetRemoteDescriptionAsync(sdpMessage).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogInformation("[web socket] operation canceled.");
            }
            catch (Exception e)
            {
                Logger.LogInformation(e, "[web socket] exception");
                throw;
            }
            finally
            {
                //Linkをはがす
                actionBlock.Complete();
                Logger.LogInformation("[web socket] end");
            }
        }

        static MIDIMessageEvent ToMIDIMessageEvent(byte[] buf)
        {
            unsafe
            {
                var s = new Span<byte>(buf);
                fixed (byte* p = &s.GetPinnableReference())
                {
                    return *(MIDIMessageEvent*)p;
                }
            }
        }
    }
}