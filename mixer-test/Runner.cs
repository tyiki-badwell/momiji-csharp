using Momiji.Core.Configuration;
using Momiji.Core.Dll;
using Momiji.Core.Timer;
using Momiji.Core.WebMidi;
using Momiji.Core.Window;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

namespace mixerTest;

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
    private IWindowManager WindowManager { get; }
    private Param Param { get; set; }

    private bool _disposed;

    private delegate void OnDispatch();
    private readonly ActionBlock<OnDispatch> _dispatcher = new((task) => { task(); });

    private CancellationTokenSource? _processCancel;
    private Task? _processTask;
    private readonly BufferBlock<MIDIMessageEvent2> _midiEventInput = new();
    private readonly BufferBlock<MIDIMessageEvent2> _midiEventOutput = new();

    private ILogic? _logic;

    //private readonly IDictionary<WebSocket, int> webSocketPool = new ConcurrentDictionary<WebSocket, int>();

    private readonly BroadcastBlock<string> _wsBroadcaster = new(null);
    private readonly CancellationTokenSource _wsProcessCancel = new();

    //private BufferBlock<OpusOutputBuffer> audioOutput = new BufferBlock<OpusOutputBuffer>();
    //private BufferBlock<H264OutputBuffer> videoOutput = new BufferBlock<H264OutputBuffer>();

    public Runner(IConfiguration configuration, ILoggerFactory loggerFactory, IDllManager dllManager, IWindowManager windowManager)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        LoggerFactory = loggerFactory;
        Logger = LoggerFactory.CreateLogger<Runner>();
        DllManager = dllManager;
        WindowManager = windowManager;

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
        if (_disposed) return;

        if (disposing)
        {
            try
            {
                _wsProcessCancel.Cancel();
            }
            catch (AggregateException e)
            {
                Logger.LogInformation(e, "[home] WebSocket Process Cancel Exception");
            }
            _wsProcessCancel.Dispose();
            //wsProcessCancel = null;

            Cancel();

            _dispatcher.Complete();
            _dispatcher.Completion.Wait();

            _processTask?.Wait();
            _processTask?.Dispose();
            _processTask = null;
            _processCancel?.Dispose();
            _processCancel = null;
        }
        _disposed = true;
    }

    public void Start()
    {
        _dispatcher.Post(async () =>
        {
            if (_processCancel != null)
            {
                Logger.LogInformation("[home] already started.");
                return;
            }
            _processCancel = new CancellationTokenSource();

            BroadcastStatus("start");
            Logger.LogInformation("main loop start");

            try
            {
                //logic = new Logic1(Configuration, LoggerFactory, DllManager, Param, midiEventInput, midiEventOutput, processCancel);
                _logic = new Logic2(Configuration, LoggerFactory, DllManager, WindowManager, Param, _midiEventInput, _midiEventOutput, _processCancel);
                //logic = new Logic4(Configuration, LoggerFactory, DllManager, Param, midiEventInput, midiEventOutput, processCancel);

                _processTask = _logic.RunAsync();

                BroadcastStatus("run");
                Logger.LogInformation("[home] started.");

                await _processTask.ContinueWith((task) => {

                    Cancel();

                    Logger.LogInformation(task.Exception, $"[home] task end");
                    _processTask = default;

                    _processCancel?.Dispose();
                    _processCancel = default;

                    BroadcastStatus("stop");
                    Logger.LogInformation("[home] stopped.");

                }, CancellationToken.None).ConfigureAwait(false);

                Logger.LogInformation("main loop end");
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
        });
    }

    public void Cancel()
    {
        _dispatcher.Post(() =>
        {
            if (_processCancel == null)
            {
                Logger.LogInformation("[home] already stopped.");
                return;
            }

            _processCancel?.Cancel();
        });
    }

    public void OpenEditor()
    {
        _dispatcher.Post(() =>
        {
            if (_processCancel == null)
            {
                Logger.LogInformation("[home] already stopped.");
                return;
            }

            _logic?.OpenEditor();
        });
    }

    public void CloseEditor()
    {
        _dispatcher.Post(() =>
        {
            if (_processCancel == null)
            {
                Logger.LogInformation("[home] already stopped.");
                return;
            }

            _logic?.CloseEditor();
        });
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

        _wsBroadcaster.Post(JsonSerializer.Serialize(param, new JsonSerializerOptions()));
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

        var ct = _wsProcessCancel.Token;

        try
        {
            using var unlink = _wsBroadcaster.LinkTo(new ActionBlock<string>(async message =>
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
                    _midiEventInput.Post(midiEvent2);
                    _midiEventOutput.Post(midiEvent2);
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    var bufArray = buf.Array;
                    if (bufArray == default)
                    {
                        continue;
                    }

                    var text = Encoding.UTF8.GetString(bufArray, 0, result.Count).Trim();
                    Logger.LogInformation($"[web socket] text [{text}]");

                    var json = JsonSerializer.Deserialize<IDictionary<string, JsonElement>>(text);
                    if (json == default)
                    {
                        continue;
                    }

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
                        if (param != default)
                        {
                            Param = param;
                        }
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

    static MIDIMessageEvent ToMIDIMessageEvent(byte[]? buf)
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
