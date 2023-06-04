using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Debug;
using Momiji.Internal.Log;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

public class WindowManager : IDisposable, IWindowManager
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly object _sync = new();
    private CancellationTokenSource? _processCancel;
    private Task? _processTask;
    private int _uiThreadId;

    private readonly PinnedDelegate<User32.WNDPROC> _wndProc;
    private readonly WindowClass _windowClass;

    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ManualResetEventSlim _queueEvent = new();

    private readonly ConcurrentDictionary<nint, NativeWindow> _windowMap = new();
    private readonly ConcurrentDictionary<nint, NativeWindow> _windowHashMap = new();

    public WindowManager(
//        IConfiguration configuration,
        ILoggerFactory loggerFactory
    )
    {
//        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        //TODO windowとthreadが1:1のモード
//        _configurationSection = configuration.GetSection($"{typeof(WindowManager).FullName}");

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowManager>();

        _wndProc = new PinnedDelegate<User32.WNDPROC>(new(WndProc));
        _windowClass =
            new WindowClass(
                _loggerFactory,
                _wndProc,
                User32.WNDCLASSEX.CS.OWNDC
            );
    }
    ~WindowManager()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger.LogInformation("disposing");
            try
            {
                Cancel();
            }
            finally
            {
//                _desktop?.Close();
//                _windowStation?.Close();

                _windowClass.Dispose();
                _wndProc.Dispose();
            }
        }

        _disposed = true;
    }

    public void Cancel()
    {
        var processCancel = _processCancel;
        if (processCancel == null)
        {
            _logger.LogInformation("already stopped.");
            return;
        }

        var task = _processTask;
        try
        {
            processCancel.Cancel();
            task?.Wait();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "failed.");
        }
    }

    internal T Dispatch<T>(Func<T> item)
    {
        if (_uiThreadId == Environment.CurrentManagedThreadId)
        {
            _logger.LogTrace("Dispatch called from same thread id then immidiate mode");
            return item.Invoke();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.AttachedToParent);
        Dispatch(() => {
            try
            {
                tcs.SetResult(item.Invoke());
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        if (!tcs.Task.Wait(5000, CancellationToken.None))
        {
            _logger.LogError("Dispatch timeout");
        }
        return tcs.Task.Result;
    }


    private void Dispatch(Action item)
    {
        _logger.LogWithThreadId(LogLevel.Information, "Dispatch", Environment.CurrentManagedThreadId);
        if (_processCancel == default)
        {
            throw new WindowException("message loop is not exists.");
        }

        _queue.Enqueue(item);
        _queueEvent.Set();
    }

    public IWindow CreateWindow(
        IWindowManager.OnPreCloseWindow? onPreCloseWindow = default,
        IWindowManager.OnPostPaint? onPostPaint = default
    )
    {
        var window =
            new NativeWindow(
                _loggerFactory,
                this,
                onPreCloseWindow,
                onPostPaint
            );

        _windowHashMap.TryAdd(window.GetHashCode(), window);

        window.CreateWindow(_windowClass);

        return window;
    }

    public void CloseAll()
    {
        foreach (var window in _windowMap.Values)
        {
            try
            {
                window.Close();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "close failed.");
            }
        }

/*
        _windowMap.Values.AsParallel().ForAll(window => {
            try
            {
                window.Close();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "close failed.");
            }
        });
*/
    }

    public async Task StartAsync(CancellationToken stoppingToken)
    {
        lock (_sync)
        {
            if (_processCancel != null)
            {
                _logger.LogInformation("already started.");
                return;
            }
            _processCancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        }

        _processTask = Run();

        try
        {
            await _processTask.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "process task failed.");
        }

        _logger.LogWithThreadId(LogLevel.Information, "process task end", Environment.CurrentManagedThreadId);

        Cancel();
        _logger.LogTrace("cancel end");

        _processTask = default;

        _processCancel?.Dispose();
        _processCancel = default;

        _logger.LogInformation("stopped.");
    }

    private Task Run()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);
        var thread = new Thread(() =>
        {
            _logger.LogInformation("thread start");
            Exception? error = default; 
            try
            {
                WindowDebug.CheckIntegrityLevel(_loggerFactory);
                WindowDebug.CheckDesktop(_loggerFactory);
                WindowDebug.CheckGetProcessInformation(_loggerFactory);

                MessageLoop();
                _logger.LogWithThreadId(LogLevel.Information, "message loop normal end", Environment.CurrentManagedThreadId);
            }
            catch (Exception e)
            {
                error = e;
                _logger.LogError(e, "message loop exception");
                _processCancel?.Cancel();
            }

            try
            {
                if (!_windowMap.IsEmpty)
                {
                    //クローズできていないwindowが残っているのは異常事態
                    _logger.LogWarning($"window left {_windowMap.Count}");

                    foreach (var item in _windowMap)
                    {
                        //TODO closeした通知を流す必要あり

                        _logger.LogWarning($"DestroyWindow {item.Key:X}");
                        User32.DestroyWindow(item.Key);
                    }

                    _windowMap.Clear();
                }
            }
            catch (Exception e)
            {
                error = e;
                _logger.LogError(e, "window clean up exception");
            }

            if (error != default)
            {
                tcs.SetException(new WindowException("message loop exception", error));
            }
            else
            {
                tcs.SetResult();
            }

            _uiThreadId = default;
        })
        {
            IsBackground = false,
            Name = "UI Thread"
        };

        thread.SetApartmentState(ApartmentState.STA);
        _logger.LogInformation($"GetApartmentState {thread.GetApartmentState()}");
        _uiThreadId = thread.ManagedThreadId;
        thread.Start();

        //このタスクでcontinue withすると、UIスレッドでQueue登録してスレッド終了し、QueueのCOMアクセスが失敗する
        return tcs.Task;
    }

    private void MessageLoop()
    {
        if (_processCancel == null)
        {
            throw new InvalidOperationException($"{nameof(_processCancel)} is null.");
        }

        {
            var result = User32.IsGUIThread(true);
            if (!result)
            {
                var error = Marshal.GetLastPInvokeError();
                throw new WindowException($"IsGUIThread failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }
        }

        { //メッセージキューが無ければ作られるハズ
            var result = 
                User32.GetQueueStatus(
                    0x04FF //QS_ALLINPUT
                );
            var error = Marshal.GetLastPInvokeError();
            _logger.LogInformation($"GetQueueStatus {result} [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
        }

        {
            var si = new Kernel32.STARTUPINFOW()
            {
                cb = Marshal.SizeOf<Kernel32.STARTUPINFOW>()
            };
            Kernel32.GetStartupInfoW(ref si);
            var error = Marshal.GetLastPInvokeError();
            _logger.LogInformation($"GetStartupInfoW [{si.dwFlags}][{si.wShowWindow}] [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
        }

        var forceCancel = false;
        var cancelled = false;

        var ct = _processCancel.Token;

        using var waitHandlesPin = new PinnedBuffer<nint[]>(new nint[] {
            _queueEvent.WaitHandle.SafeWaitHandle.DangerousGetHandle(),
            ct.WaitHandle.SafeWaitHandle.DangerousGetHandle()
         });
        var handleCount = waitHandlesPin.Target.Length;

        _logger.LogWithThreadId(LogLevel.Information, "start message loop", Environment.CurrentManagedThreadId);
        while (true)
        {
            if (forceCancel)
            {
                _logger.LogInformation("force canceled.");
                break;
            }

            if (cancelled)
            {
                if (_windowMap.IsEmpty)
                {
                    _logger.LogInformation("all closed.");
                    break;
                }
            }
            else if (ct.IsCancellationRequested)
            {
                cancelled = true;

                _logger.LogInformation("canceled.");
                CloseAll();

                // 10秒以内にクローズされなければ、ループを終わらせる
                var _ =
                    Task.Delay(10000, CancellationToken.None)
                    .ContinueWith(
                        (task) => { forceCancel = true; },
                        TaskScheduler.Default
                    );
            }

            {
                _logger.LogWithThreadId(LogLevel.Trace, "MsgWaitForMultipleObjectsEx", Environment.CurrentManagedThreadId);
                var res =
                    User32.MsgWaitForMultipleObjectsEx(
                        (uint)handleCount,
                        waitHandlesPin.AddrOfPinnedObject,
                        1000,
                        0x04FF, //QS_ALLINPUT
                        0x0004 //MWMO_INPUTAVAILABLE
                    );
                if (res == 258) // WAIT_TIMEOUT
                {
                    _logger.LogTrace("MsgWaitForMultipleObjectsEx timeout.");
                    continue;
                }
                else if (res == 0) // WAIT_OBJECT_0
                {
                    _logger.LogTrace("MsgWaitForMultipleObjectsEx comes queue event.");
                    _queueEvent.Reset();
                    //ディスパッチ
                    while (_queue.TryDequeue(out var result))
                    {
                        _logger.LogWithThreadId(LogLevel.Trace, "Invoke", Environment.CurrentManagedThreadId);
                        result.Invoke();
                    }
                    continue;
                }
                else if (res == 1) // WAIT_OBJECT_0+1
                {
                    _logger.LogTrace("MsgWaitForMultipleObjectsEx comes cancel event.");
                    //ctがシグナル状態になりっぱなしになるので、リストから外す
                    handleCount--;
                    continue;
                }
                else if (res == handleCount) // WAIT_OBJECT_0+2
                {
                    _logger.LogTrace("MsgWaitForMultipleObjectsEx comes message.");
                    DispatchMessage();
                    continue;
                }
                else
                {
                    var error = Marshal.GetLastPInvokeError();
                    throw new WindowException($"MsgWaitForMultipleObjectsEx failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
                }
            }
        }
        _logger.LogInformation("end message loop.");
    }

    private void DispatchMessage()
    {
        var msg = new User32.MSG();

        while (true)
        {
            _logger.LogWithThreadId(LogLevel.Trace, "PeekMessage", Environment.CurrentManagedThreadId);
            if (!User32.PeekMessageW(
                    ref msg,
                    nint.Zero,
                    0,
                    0,
                    0x0001 // PM_REMOVE
            ))
            {
                _logger.LogTrace("PeekMessage NONE");
                return;
            }
            _logger.LogTrace($"MSG {msg}");

            var IsWindowUnicode = (msg.hwnd != nint.Zero) && User32.IsWindowUnicode(msg.hwnd);
            _logger.LogTrace($"IsWindowUnicode {IsWindowUnicode}");

            {
                var ret = User32.InSendMessageEx(nint.Zero);
                _logger.LogTrace($"InSendMessageEx {ret:X}");
                if ((ret & (0x00000008 | 0x00000001)) == 0x00000001) //ISMEX_SEND
                {
                    _logger.LogTrace("ISMEX_SEND");
                    var ret2 = User32.ReplyMessage(new nint(1));
                    var error = Marshal.GetLastPInvokeError();
                    _logger.LogTrace($"ReplyMessage {ret2} [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
                }
            }

            //TODO: msg.hwnd がnullのときは、↓以降を行っても意味はないらしい？

            {
                _logger.LogTrace("TranslateMessage");
                var ret = User32.TranslateMessage(ref msg);
                var error = Marshal.GetLastPInvokeError();
                _logger.LogTrace($"TranslateMessage {ret} [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }

            {
                _logger.LogTrace("DispatchMessage");
                var ret = IsWindowUnicode
                            ? User32.DispatchMessageW(ref msg)
                            : User32.DispatchMessageA(ref msg)
                            ;
                var error = Marshal.GetLastPInvokeError();
                _logger.LogTrace($"DispatchMessage {ret} [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }
        }
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var isWindowUnicode = (hwnd != nint.Zero) && User32.IsWindowUnicode(hwnd);
        _logger.LogMsgWithThreadId(LogLevel.Trace, "WndProc", hwnd, msg, wParam, lParam, Environment.CurrentManagedThreadId);

        switch (msg)
        {
            case 0x0081://WM_NCCREATE
                _logger.LogTrace("WM_NCCREATE");

                nint windowHashCode;
                unsafe
                {
                    var cr = Unsafe.AsRef<User32.CREATESTRUCT>((void*)lParam);
                    _logger.LogTrace($"CREATESTRUCT {cr.lpCreateParams:X} {cr.hwndParent:X} {cr.cy} {cr.cx} {cr.y} {cr.x} {cr.style:X} {cr.dwExStyle:X}");
                    windowHashCode = cr.lpCreateParams;
                }

                if (_windowHashMap.TryRemove(windowHashCode, out var window))
                {
                    window._hWindow = hwnd;
                    _windowMap.TryAdd(hwnd, window);
                    _logger.LogInformation($"add window map [{hwnd:X}]");
                }
                else
                {
                    _logger.LogWarning("unkown window handle");
                }
                break;
        }

        {
            if (_windowMap.TryGetValue(hwnd, out var window))
            {
                //ウインドウに流す
                var result = window.WndProc(msg, wParam, lParam, out var handled);
                if (handled)
                {
                    return result;
                }
            }
            else
            {
                _logger.LogTrace("unkown window handle");
            }
        }

        switch (msg)
        {
            case 0x0082://WM_NCDESTROY
                _logger.LogTrace("WM_NCDESTROY");
                if (_windowMap.TryRemove(hwnd, out _))
                {
                    _logger.LogInformation($"remove window map [{hwnd:X}]");
                }
                else
                {
                    _logger.LogWarning($"failed. remove window map [{hwnd:X}]");
                }
                break;
        }

        var defWndProcResult = isWindowUnicode
            ? User32.DefWindowProcW(hwnd, msg, wParam, lParam)
            : User32.DefWindowProcA(hwnd, msg, wParam, lParam)
            ;

        _logger.LogTrace($"DefWindowProc [{defWndProcResult:X}]");
        return defWndProcResult;
    }

}
