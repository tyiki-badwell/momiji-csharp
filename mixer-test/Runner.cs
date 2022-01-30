﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Configuration;
using Momiji.Core.Dll;
using Momiji.Core.Timer;
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
        void Start();
        void Cancel();
        void OpenEditor();
        void CloseEditor();

        //void Note(MIDIMessageEvent[] midiMessage);
        Task AcceptWebSocket(WebSocket webSocket);
    }

    public interface ILogic
    {
        Task RunAsync();
        void OpenEditor();
        void CloseEditor();
    }

    public class Runner : IRunner, IDisposable
    {
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private IDllManager DllManager { get; }
        private Param Param { get; set; }

        private bool disposed;

        private delegate void OnDispatch();
        private readonly ActionBlock<OnDispatch> dispatcher = new((task) => { task(); });

        private CancellationTokenSource processCancel;
        private Task processTask;
        private readonly BufferBlock<MIDIMessageEvent2> midiEventInput = new();
        private readonly BufferBlock<MIDIMessageEvent2> midiEventOutput = new();

        private ILogic logic;

        //private readonly IDictionary<WebSocket, int> webSocketPool = new ConcurrentDictionary<WebSocket, int>();

        private readonly BroadcastBlock<string> wsBroadcaster = new(null);
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
            Configuration.GetSection(typeof(Param).FullName).Bind(param);
            Param = param;

            BroadcastStatus("stop");
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

                dispatcher.Complete();
                dispatcher.Completion.Wait();
            }
            disposed = true;
        }

        public void Start()
        {
            dispatcher.Post(() =>
            {
                if (processCancel != null)
                {
                    Logger.LogInformation("[home] already started.");
                    return;
                }
                processCancel = new CancellationTokenSource();
                processTask = Loop().ContinueWith((task)=> { Cancel(); }, TaskScheduler.Default);

                Logger.LogInformation("[home] started.");
            });
        }


        public void Cancel()
        {
            dispatcher.Post(() =>
            {
                if (processCancel == null)
                {
                    Logger.LogInformation("[home] already stopped.");
                    return;
                }

                try
                {
                    processCancel.Cancel();
                    processTask.Wait();
                }
                catch (AggregateException e)
                {
                    Logger.LogInformation(e, "[home] Process Cancel Exception");
                }
                finally
                {
                    processTask?.Dispose();
                    processTask = null;
                    processCancel?.Dispose();
                    processCancel = null;
                }
                Logger.LogInformation("[home] stopped.");
            });
        }

        public void OpenEditor()
        {
            dispatcher.Post(() =>
            {
                if (processCancel == null)
                {
                    Logger.LogInformation("[home] already stopped.");
                    return;
                }

                logic.OpenEditor();
            });
        }

        public void CloseEditor()
        {
            dispatcher.Post(() =>
            {
                if (processCancel == null)
                {
                    Logger.LogInformation("[home] already stopped.");
                    return;
                }

                logic.CloseEditor();
            });
        }

        private async Task Loop()
        {
            BroadcastStatus("start");
            Logger.LogInformation("main loop start");

            try
            {
                //logic = new Logic1(Configuration, LoggerFactory, DllManager, Param, midiEventInput, midiEventOutput, processCancel);
                logic = new Logic2(Configuration, LoggerFactory, DllManager, Param, midiEventInput, midiEventOutput, processCancel);
                //logic = new Logic4(Configuration, LoggerFactory, DllManager, Param, midiEventInput, midiEventOutput, processCancel);

                var task = logic.RunAsync();

                BroadcastStatus("run");

                await task.ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                Logger.LogInformation("TaskCanceled");
            }
            catch (Exception e)
            {
                Logger.LogInformation(e, "Exception");
                throw;
            }
            finally
            {
                BroadcastStatus("stop");
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
                midiEventInput.Post(midiEvent);
            }
        }
        */

        private void BroadcastStatus(string status)
        {
            var param = new Dictionary<string, object>
            {
                ["type"] = "status",
                ["value"] = status
            };

            wsBroadcaster.Post(JsonSerializer.Serialize(param, new JsonSerializerOptions()));
        }

        private static async Task SendWebsocketAsync(WebSocket webSocket, IDictionary<string, object> param, CancellationToken ct)
        {
            await SendWebsocketAsync(webSocket, JsonSerializer.Serialize(param, new JsonSerializerOptions()), ct).ConfigureAwait(false);
        }
        private static async Task SendWebsocketAsync(WebSocket webSocket, string param, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(param);
            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }

        public async Task AcceptWebSocket(WebSocket webSocket)
        {
            if (webSocket == default)
            {
                throw new ArgumentNullException(nameof(webSocket));
            }

            Logger.LogInformation("[web socket] start");

            var ct = wsProcessCancel.Token;

            try
            {
                using var unlink = wsBroadcaster.LinkTo(new ActionBlock<string>(async message =>
                {
                    await SendWebsocketAsync(webSocket, message, ct).ConfigureAwait(false);
                }));

                var counter = new ElapsedTimeCounter();
                var buf = WebSocket.CreateServerBuffer(1024);

                await SendWebsocketAsync(webSocket, new Dictionary<string, object>
                {
                    ["type"] = "param",
                    ["param"] = Param
                }, ct).ConfigureAwait(false);

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
                        midiEvent2.receivedTimeUSec = counter.NowTicks / 10;
                        midiEventInput.Post(midiEvent2);
                        midiEventOutput.Post(midiEvent2);
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var text = Encoding.UTF8.GetString(buf.Array, 0, result.Count).Trim();
                        Logger.LogInformation($"[web socket] text [{text}]");

                        var json = JsonSerializer.Deserialize<IDictionary<string, JsonElement>>(text);
                        var type = json["type"].GetString();
                        Logger.LogInformation($"[web socket] type = {type}.");

                        if (type == "start")
                        {
                            Start();
                        }
                        else if (type == "cancel")
                        {
                            Cancel();
                        }
                        else if (type == "openeditor")
                        {
                            OpenEditor();
                        }
                        else if (type == "closeeditor")
                        {
                            CloseEditor();
                        }
                        else if (type == "close")
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "close request", ct).ConfigureAwait(false);
                            break;
                        }
                        else if (type == "read")
                        {
                            await SendWebsocketAsync(webSocket, new Dictionary<string, object>
                            {
                                ["type"] = "param",
                                ["param"] = Param
                            }, ct).ConfigureAwait(false);
                        }
                        else if (type == "write")
                        {
                            var paramJson = json["param"].GetRawText();
                            var options = new JsonSerializerOptions()
                            {
                                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                            };

                            var param = JsonSerializer.Deserialize<Param>(paramJson, options);
                            Param = param;
                        }
                        else if (type == "offer")
                        {
                            var sdp = json["sdp"];
                            Logger.LogInformation($"[web socket] sdp = {sdp}");
                        }
                        else if (type == "answer")
                        {
                            var sdp = json["sdp"];
                            Logger.LogInformation($"[web socket] sdp = {sdp}");
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