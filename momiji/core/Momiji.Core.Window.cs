using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Interop.Vst;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

public class WindowException : Exception
{
    public WindowException()
    {
    }

    public WindowException(string message) : base(message)
    {
    }

    public WindowException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public interface IWindowManager
{
    Task StartAsync(CancellationToken stoppingToken);
    void Cancel();

    public delegate void OnPreCloseWindow();
    public delegate void OnPostPaint(IntPtr hWindow);

    public IWindow CreateWindow(
        OnPreCloseWindow? onPreCloseWindow = default,
        OnPostPaint? onPostPaint = default
    );

    void CloseAll();
}

public interface IWindow
{
    IntPtr Handle { get; }
    T Dispatch<T>(Func<T> item);
    bool Close();
    bool Move(
        int x,
        int y,
        int width,
        int height,
        bool repaint
    );

    bool Show(
        int cmdShow
    );
}


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

    private readonly ConcurrentDictionary<IntPtr, NativeWindow> _windowMap = new();
    private readonly ConcurrentDictionary<int, NativeWindow> _windowHashMap = new();

    public WindowManager(
        ILoggerFactory loggerFactory
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowManager>();

        _wndProc = new PinnedDelegate<User32.WNDPROC>(new(WndProc));
        _windowClass =
            new WindowClass(
                _loggerFactory,
                _wndProc,
                User32.WNDCLASS.CS.OWNDC
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
            _logger.LogInformation("[window manager] disposing");
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
            _logger.LogInformation("[window manager] already stopped.");
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
            _logger.LogError(e, "[window manager] failed.");
        }
    }

    internal T Dispatch<T>(Func<T> item)
    {
        if (_uiThreadId == Environment.CurrentManagedThreadId)
        {
            _logger.LogTrace("[window manager] Dispatch called from same thread id then immidiate mode");
            return item.Invoke();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.AttachedToParent);
        Dispatch(() => {
            try
            {
                var result = item.Invoke();
                tcs.SetResult(result);
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        if (!tcs.Task.Wait(5000, CancellationToken.None))
        {
            _logger.LogError("[window manager] Dispatch timeout");
        }
        return tcs.Task.Result;
    }


    private void Dispatch(Action item)
    {
        _logger.LogInformation("[window manager] Dispatch {CurrentManagedThreadId:X}", Environment.CurrentManagedThreadId);
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
                _logger.LogError(e, "[window manager] close failed.");
            }
        }
    }

    public async Task StartAsync(CancellationToken stoppingToken)
    {
        lock (_sync)
        {
            if (_processCancel != null)
            {
                _logger.LogInformation("[window manager] already started.");
                return;
            }
            _processCancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        }
        _processTask = Run();

        try
        {
            await _processTask.ContinueWith((task) =>
            {
                Cancel();

                _logger.LogInformation(task.Exception, "[window manager] task end");

                _processTask = default;

                _processCancel?.Dispose();
                _processCancel = default;

                _logger.LogInformation("[window manager] stopped.");

            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "[window manager] failed.");
        }
    }

    private async Task Run()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.AttachedToParent);
        var thread = new Thread(() =>
        {
            try
            {
                WindowDebug.CheckIntegrityLevel(_loggerFactory);
                WindowDebug.CheckDesktop(_loggerFactory);

                MessageLoop();
                _logger.LogInformation("[window manager] message loop normal end");
                tcs.SetResult(true);
            }
            catch (Exception e)
            {
                _logger.LogInformation(e, "[window manager] message loop exception");
                _processCancel?.Cancel();
                tcs.SetException(new WindowException("message loop exception", e));
            }
        })
        {
            IsBackground = false,
            Name = "UI Thread"
        };

        thread.SetApartmentState(ApartmentState.STA);
        _logger.LogInformation("[window manager] GetApartmentState {GetApartmentState}", thread.GetApartmentState());
        _uiThreadId = thread.ManagedThreadId;
        thread.Start();

        await tcs.Task.ContinueWith((_) => {
            _logger.LogInformation("[window manager] message loop task end");
            _uiThreadId = default;
        }).ConfigureAwait(false);

        var _ = tcs.Task.Result;

        if (!_windowMap.IsEmpty)
        {
            _logger.LogWarning("[window manager] window left {Count}", _windowMap.Count);
            foreach (var hwnd in _windowMap.Keys)
            {
                User32.DestroyWindow(hwnd);
            }
        }

    }

    private void MessageLoop()
    {
        if (_processCancel == null)
        {
            throw new InvalidOperationException($"{nameof(_processCancel)} is null.");
        }

        {
            var result = User32.IsGUIThread(true);
            _logger.LogInformation("[window manager] IsGUIThread {result} {GetLastWin32Error}", result, Marshal.GetLastWin32Error());
        }

        { //メッセージキューが無ければ作られるハズ
            var result = 
                User32.GetQueueStatus(
                    0x04FF //QS_ALLINPUT
                );
            _logger.LogInformation("[window manager] GetQueueStatus {result} {GetLastWin32Error}", result, Marshal.GetLastWin32Error());
        }

        {
            var si = new Kernel32.STARTUPINFOW()
            {
                cb = Marshal.SizeOf<Kernel32.STARTUPINFOW>()
            };
            Kernel32.GetStartupInfoW(ref si);

            _logger.LogInformation("[window manager] GetStartupInfoW [{dwFlags}][{wShowWindow}] {GetLastWin32Error}", si.dwFlags, si.wShowWindow, Marshal.GetLastWin32Error());
        }

        var forceCancel = false;
        var cancelled = false;

        var ct = _processCancel.Token;

        using var waitHandlesPin = new PinnedBuffer<IntPtr[]>(new IntPtr[] {
            _queueEvent.WaitHandle.SafeWaitHandle.DangerousGetHandle(),
            ct.WaitHandle.SafeWaitHandle.DangerousGetHandle()
         });
        var handleCount = waitHandlesPin.Target.Length;

        _logger.LogInformation("[window manager] start message loop. current {CurrentManagedThreadId:X}", Environment.CurrentManagedThreadId);
        while (true)
        {
            if (forceCancel)
            {
                _logger.LogInformation("[window manager] force canceled.");
                break;
            }

            if (cancelled)
            {
                if (_windowMap.IsEmpty)
                {
                    _logger.LogInformation("[window manager] all closed.");
                    break;
                }
            }
            else if (ct.IsCancellationRequested)
            {
                cancelled = true;

                _logger.LogInformation("[window manager] canceled.");
                CloseAll();

                // 5秒以内にクローズされなければ、ループを終わらせる
                var _ =
                    Task.Delay(5000, CancellationToken.None)
                    .ContinueWith(
                        (task) => { forceCancel = true; },
                        TaskScheduler.Default
                    );
            }

            {
                _logger.LogTrace("[window manager] MsgWaitForMultipleObjectsEx current {CurrentManagedThreadId:X}", Environment.CurrentManagedThreadId);
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
                    _logger.LogTrace("[window manager] MsgWaitForMultipleObjectsEx timeout.");
                    continue;
                }
                else if (res == 0) // WAIT_OBJECT_0
                {
                    _logger.LogTrace("[window manager] MsgWaitForMultipleObjectsEx comes queue event.");
                    _queueEvent.Reset();
                    //ディスパッチ
                    while (_queue.TryDequeue(out var result))
                    {
                        _logger.LogTrace("[window manager] Invoke current {CurrentManagedThreadId:X}", Environment.CurrentManagedThreadId);
                        result.Invoke();
                    }
                    continue;
                }
                else if (res == 1) // WAIT_OBJECT_0+1
                {
                    _logger.LogTrace("[window manager] MsgWaitForMultipleObjectsEx comes cancel event.");
                    //ctがシグナル状態になりっぱなしになるので、リストから外す
                    handleCount--;
                    continue;
                }
                else if (res == handleCount) // WAIT_OBJECT_0+2
                {
                    _logger.LogTrace("[window manager] MsgWaitForMultipleObjectsEx comes message.");
                    DispatchMessage();
                    continue;
                }
                else
                {
                    throw new WindowException($"MsgWaitForMultipleObjectsEx failed {res} {Marshal.GetLastWin32Error()}");
                }
            }
        }
        _logger.LogInformation("[window manager] end message loop.");
    }

    private void DispatchMessage()
    {
        var msg = new User32.MSG();

        while (true)
        {
            _logger.LogTrace("[window manager] PeekMessage current {CurrentManagedThreadId:X}", Environment.CurrentManagedThreadId);
            if (!User32.PeekMessageW(
                    ref msg,
                    IntPtr.Zero,
                    0,
                    0,
                    0x0001 // PM_REMOVE
            ))
            {
                _logger.LogTrace("[window manager] PeekMessage NONE");
                return;
            }
            _logger.LogTrace("[window manager] MSG {hwnd:X} {message:X} {wParam:X} {lParam:X} {time} {pt_x} {pt_y}", msg.hwnd, msg.message, msg.wParam, msg.lParam, msg.time, msg.pt_x, msg.pt_y);

            var IsWindowUnicode = (msg.hwnd != IntPtr.Zero) && User32.IsWindowUnicode(msg.hwnd);
            _logger.LogTrace("[window] IsWindowUnicode {IsWindowUnicode}", IsWindowUnicode);

            {
                var ret = User32.InSendMessageEx(IntPtr.Zero);
                _logger.LogTrace("[window manager] InSendMessageEx {ret:X}", ret);
                if ((ret & (0x00000008 | 0x00000001)) == 0x00000001) //ISMEX_SEND
                {
                    _logger.LogTrace("[window manager] ISMEX_SEND");
                    var ret2 = User32.ReplyMessage(new IntPtr(1));
                    _logger.LogTrace("[window manager] ReplyMessage {ret2} {GetLastWin32Error}", ret2, Marshal.GetLastWin32Error());
                }
            }

            //TODO: msg.hwnd がnullのときは、↓以降を行っても意味はないらしい？

            {
                _logger.LogTrace("[window manager] TranslateMessage");
                var ret = User32.TranslateMessage(ref msg);
                _logger.LogTrace("[window manager] TranslateMessage {ret} {GetLastWin32Error}", ret, Marshal.GetLastWin32Error());
            }

            {
                _logger.LogTrace("[window manager] DispatchMessage");
                var ret = IsWindowUnicode
                            ? User32.DispatchMessageW(ref msg)
                            : User32.DispatchMessageA(ref msg)
                            ;
                _logger.LogTrace("[window manager] DispatchMessage {ret} {GetLastWin32Error}", ret, Marshal.GetLastWin32Error());
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var isWindowUnicode = (hwnd != IntPtr.Zero) && User32.IsWindowUnicode(hwnd);
        _logger.LogTrace("[window manager] WndProc[{hwnd:X} {msg:X} {wParam:X} {lParam:X}] current {CurrentManagedThreadId:X}", hwnd, msg, wParam, lParam, Environment.CurrentManagedThreadId);

        switch (msg)
        {
            case 0x0081://WM_NCCREATE
                _logger.LogTrace("[window manager] WM_NCCREATE");

                int windowHashCode;
                unsafe
                {
                    var cr = Unsafe.AsRef<User32.CREATESTRUCT>((void*)lParam);
                    _logger.LogTrace($"[window manager] CREATESTRUCT {cr.lpCreateParams:X} {cr.hwndParent:X} {cr.cy} {cr.cx} {cr.y} {cr.x} {cr.style:X} {cr.dwExStyle:X}");
                    windowHashCode = (int)cr.lpCreateParams;
                }

                if (_windowHashMap.TryRemove(windowHashCode, out var window))
                {
                    _windowMap.TryAdd(hwnd, window);
                    _logger.LogInformation("[window manager] add window map [{hwnd:X}]", hwnd);
                }
                else
                {
                    _logger.LogWarning("[window manager] unkown window handle");
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
                _logger.LogTrace("[window manager] unkown window handle");
            }
        }

        switch (msg)
        {
            case 0x0082://WM_NCDESTROY
                _logger.LogTrace("[window manager] WM_NCDESTROY");
                if (_windowMap.TryRemove(hwnd, out _))
                {
                    _logger.LogInformation("[window manager] remove window map [{hwnd:X}]", hwnd);
                }
                else
                {
                    _logger.LogWarning("[window manager] failed. remove window map [{hwnd:X}]", hwnd);
                }
                break;
        }

        var defWndProcResult = isWindowUnicode
            ? User32.DefWindowProcW(hwnd, msg, wParam, lParam)
            : User32.DefWindowProcA(hwnd, msg, wParam, lParam)
            ;

        _logger.LogTrace("[window manager] DefWindowProc [{defWndProcResult:X}]", defWndProcResult);
        return defWndProcResult;
    }

}


internal class WindowClass : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private User32.WNDCLASS _windowClass;

    internal IntPtr ClassName => _windowClass.lpszClassName;

    internal IntPtr HInstance => _windowClass.hInstance;

    internal WindowClass(
        ILoggerFactory loggerFactory,
        PinnedDelegate<User32.WNDPROC> wndProc,
        User32.WNDCLASS.CS cs = User32.WNDCLASS.CS.NONE
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowClass>();

        _windowClass = new User32.WNDCLASS
        {
            style = cs,
            lpfnWndProc = wndProc.FunctionPointer,
            hInstance = Kernel32.GetModuleHandle(default),
            lpszClassName = Marshal.StringToHGlobalUni(nameof(WindowClass) + Guid.NewGuid().ToString())
        };

        var atom = User32.RegisterClassW(ref _windowClass);
        var error = Marshal.GetLastWin32Error();
        _logger.LogInformation("[window class] RegisterClass {lpszClassName} {atom} {GetLastWin32Error}", _windowClass.lpszClassName, atom, error);
        if (atom == 0)
        {
            throw new WindowException($"RegisterClass failed [{error}]");
        }
    }

    ~WindowClass()
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
            _logger.LogInformation("[window class] disposing");
        }

        var result = User32.UnregisterClassW(_windowClass.lpszClassName, _windowClass.hInstance);
        _logger.LogInformation("[window class] UnregisterClass {lpszClassName} {result} {GetLastWin32Error}", _windowClass.lpszClassName, result, Marshal.GetLastWin32Error());

        Marshal.FreeHGlobal(_windowClass.lpszClassName);

        _disposed = true;
    }
}

internal class NativeWindow : IWindow
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly WindowManager _windowManager;

    private readonly IWindowManager.OnPreCloseWindow? _onPreCloseWindow;
    private readonly IWindowManager.OnPostPaint? _onPostPaint;

    private IntPtr _hWindow;
    public IntPtr Handle => _hWindow;

    private readonly ConcurrentDictionary<IntPtr, (IntPtr, PinnedDelegate<User32.WNDPROC>)> _oldWndProcMap = new();
    internal NativeWindow(
        ILoggerFactory loggerFactory,
        WindowManager windowManager,
        IWindowManager.OnPreCloseWindow? onPreCloseWindow = default,
        IWindowManager.OnPostPaint? onPostPaint = default
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<NativeWindow>();
        _windowManager = windowManager;

        _onPreCloseWindow = onPreCloseWindow;
        _onPostPaint = onPostPaint;

        _logger.LogInformation("[window] Create end");
    }

    public T Dispatch<T>(Func<T> item)
    {
        return _windowManager.Dispatch(item);
    }

    internal void CreateWindow(
        WindowClass windowClass
    )
    {
        var thisHashCode = GetHashCode();

        _hWindow = Dispatch(() => {
            var style = unchecked((int)
                0x80000000 //WS_POPUP
                           // 0x00000000 //WS_OVERLAPPED
                           // | 0x00C00000 //WS_CAPTION
                           // | 0x00080000 //WS_SYSMENU
                           // | 0x00040000 //WS_THICKFRAME
                | 0x10000000 //WS_VISIBLE
                );

            var CW_USEDEFAULT = unchecked((int)0x80000000);

            _logger.LogTrace("[window] CreateWindowEx current {CurrentManagedThreadId:X}", Environment.CurrentManagedThreadId);
            var hWindow =
                User32.CreateWindowExW(
                    0,
                    windowClass.ClassName,
                    IntPtr.Zero,
                    style,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    windowClass.HInstance,
                    new IntPtr(thisHashCode)
                );
            var error = Marshal.GetLastWin32Error();
            _logger.LogInformation("[window] CreateWindowEx result {hWindow:X} {error} current {CurrentManagedThreadId:X}", hWindow, error, Environment.CurrentManagedThreadId);
            if (hWindow == IntPtr.Zero)
            {
                hWindow = default;
                throw new WindowException($"CreateWindowEx failed {error}");
            }

            return hWindow;
        });
    }

    public bool Close()
    {
        _logger.LogInformation("[window] Close {_hWindow:X}", _hWindow);
        return SendMessage(
            0x0010, //WM_CLOSE
            IntPtr.Zero,
            IntPtr.Zero
        );

/*
        return SendMessage( //SendNotifyMessage(
            0x0112, //WM_SYSCOMMAND
            (IntPtr)0xF060, //SC_CLOSE
            IntPtr.Zero
        );
*/
    }

    private bool SendMessage(
        int nMsg,
        IntPtr wParam,
        IntPtr lParam
    )
    {
        return Dispatch(() =>
        {
            _logger.LogInformation("[window] SendMessageW {_hWindow:X} {nMsg:X} {wParam:X} {lParam:X} current {CurrentManagedThreadId:X}", _hWindow, nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
            var result =
                User32.SendMessageW(
                    _hWindow,
                    nMsg,
                    wParam,
                    lParam
                );

            var error = Marshal.GetLastWin32Error();
            if (error != 0)
            {
                _logger.LogError("[window] SendMessageW {_hWindow:X} {error}", _hWindow, error);
                return false;
            }
            return true;
        });
    }

    public bool Move(
        int x,
        int y,
        int width,
        int height,
        bool repaint
    )
    {
        return Dispatch(() =>
        {
            _logger.LogInformation("[window] MoveWindow {_hWindow:X} {x} {y} {width} {height} {repaint} current {CurrentManagedThreadId:X}", _hWindow, x, y, width, height, repaint, Environment.CurrentManagedThreadId);
            var result =
                User32.MoveWindow(
                    _hWindow,
                    x,
                    y,
                    width,
                    height,
                    repaint
                );

            if (!result)
            {
                _logger.LogError("[window] MoveWindow {_hWindow:X} {GetLastWin32Error}", _hWindow, Marshal.GetLastWin32Error());
            }
            return result;
        });
    }

    public bool Show(
        int cmdShow
    )
    {
        return Dispatch(() =>
        {
            _logger.LogInformation("[window] ShowWindow {_hWindow:X} {cmdShow} current {CurrentManagedThreadId:X}", _hWindow, cmdShow, Environment.CurrentManagedThreadId);
            var result =
                User32.ShowWindow(
                    _hWindow,
                    cmdShow
                );

            //result=0: 実行前は非表示だった/ <>0:実行前から表示されていた
            _logger.LogInformation("[window] ShowWindow {_hWindow:X} {result} {GetLastWin32Error}", _hWindow, result, Marshal.GetLastWin32Error());

            {
                var wndpl = new User32.WINDOWPLACEMENT()
                {
                    length = Marshal.SizeOf<User32.WINDOWPLACEMENT>()
                };
                User32.GetWindowPlacement(_hWindow, ref wndpl);

                _logger.LogInformation("[window] GetWindowPlacement result {cmdShow} -> {showCmd}", cmdShow, wndpl.showCmd);
            }

            return result;
        });
    }
    internal IntPtr WndProc(uint msg, IntPtr wParam, IntPtr lParam, out bool handled)
    {
        handled = false;
        _logger.LogTrace("[window] WndProc[{_hWindow:X} {msg:X} {wParam:X} {lParam:X}] current {CurrentManagedThreadId:X}", _hWindow, msg, wParam, lParam, Environment.CurrentManagedThreadId);

        switch (msg)
        {
            case 0x0082://WM_NCDESTROY
                _logger.LogTrace("[window] WM_NCDESTROY");
                _hWindow = default;
                break;

            case 0x0010://WM_CLOSE
                _logger.LogTrace("[window] WM_CLOSE");
                try
                {
                    _onPreCloseWindow?.Invoke();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "[window] onPreCloseWindow error");
                }

                _logger.LogTrace("[window] DestroyWindow {_hWindow:X} current {CurrentManagedThreadId:X}", _hWindow, Environment.CurrentManagedThreadId);
                var result = User32.DestroyWindow(_hWindow);
                if (!result)
                {
                    _logger.LogError("[window] DestroyWindow {_hWindow:X} {GetLastWin32Error}", _hWindow, Marshal.GetLastWin32Error());
                }

                handled = true;
                break;

            case 0x0210://WM_PARENTNOTIFY
                _logger.LogTrace("[window] WM_PARENTNOTIFY");
                switch ((int)wParam & 0xFFFF)
                {
                    case 0x0001: //WM_CREATE
                        {
                            _logger.LogTrace("[window] WM_PARENTNOTIFY WM_CREATE");
                            var childHWnd = lParam;
                            var isChildeWindowUnicode = (lParam != IntPtr.Zero) && User32.IsWindowUnicode(childHWnd);
                            var subWndProc = new PinnedDelegate<User32.WNDPROC>(new(SubWndProc));
                            var oldWndProc = isChildeWindowUnicode
                                                ? Environment.Is64BitProcess
                                                    ? User32.SetWindowLongPtrW(childHWnd, -4, subWndProc.FunctionPointer) //GWLP_WNDPROC
                                                    : User32.SetWindowLongW(childHWnd, -4, subWndProc.FunctionPointer)
                                                : Environment.Is64BitProcess
                                                    ? User32.SetWindowLongPtrA(childHWnd, -4, subWndProc.FunctionPointer)
                                                    : User32.SetWindowLongA(childHWnd, -4, subWndProc.FunctionPointer)
                                                ;
                            _oldWndProcMap.TryAdd(childHWnd, (oldWndProc, subWndProc));

                            break;
                        }
                    case 0x0002: //WM_DESTROY
                        {
                            _logger.LogTrace("[window] WM_PARENTNOTIFY WM_DESTROY");
                            var childHWnd = lParam;
                            if (_oldWndProcMap.TryRemove(childHWnd, out var pair))
                            {
                                var isChildeWindowUnicode = (lParam != IntPtr.Zero) && User32.IsWindowUnicode(childHWnd);
                                var _ = isChildeWindowUnicode
                                                ? Environment.Is64BitProcess
                                                    ? User32.SetWindowLongPtrW(childHWnd, -4, pair.Item1) //GWLP_WNDPROC
                                                    : User32.SetWindowLongW(childHWnd, -4, pair.Item1)
                                                : Environment.Is64BitProcess
                                                    ? User32.SetWindowLongPtrA(childHWnd, -4, pair.Item1)
                                                    : User32.SetWindowLongA(childHWnd, -4, pair.Item1)
                                                ;

                                pair.Item2.Dispose();
                            }

                            break;
                        }
                }

                break;
        }
        return IntPtr.Zero;
    }

    private IntPtr SubWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        _logger.LogTrace("[window] SubWndProc[{hwnd:X} {msg:X} {wParam:X} {lParam:X}] current {CurrentManagedThreadId:X}", hwnd, msg, wParam, lParam, Environment.CurrentManagedThreadId);

        var isWindowUnicode = (hwnd != IntPtr.Zero) && User32.IsWindowUnicode(hwnd);
        IntPtr result;

        if (_oldWndProcMap.TryGetValue(hwnd, out var pair))
        {
            _logger.LogTrace("[window] CallWindowProc [{pair.Item1:X}]", pair.Item1);
            result = isWindowUnicode
                        ? User32.CallWindowProcW(pair.Item1, hwnd, msg, wParam, lParam)
                        : User32.CallWindowProcA(pair.Item1, hwnd, msg, wParam, lParam)
                        ;
        }
        else
        {
            _logger.LogWarning("[window] unkown hwnd -> DefWindowProc");
            result = isWindowUnicode
                        ? User32.DefWindowProcW(hwnd, msg, wParam, lParam)
                        : User32.DefWindowProcA(hwnd, msg, wParam, lParam)
                        ;
        }

        switch (msg)
        {
            case 0x000F://WM_PAINT
                _logger.LogTrace("[window] SubWndProc WM_PAINT");
                try
                {
                    _onPostPaint?.Invoke(hwnd);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "[window] onPostPaint error");
                }
                break;

            default:
                break;
        }

        return result;
    }

}
        /*
        //表示していないとwinrt::hresult_invalid_argumentになる
        var item = GraphicsCaptureItemInterop.CreateForWindow(hWindow);
        item.Closed += (item, obj) => {
            Logger.LogInformation("[window] GraphicsCaptureItem closed");
        };

        unsafe
        {

            Windows.Win32.Graphics.Direct3D11.ID3D11Device* d;

            Windows.Win32.PInvoke.D3D11CreateDevice(
                null,
                Windows.Win32.Graphics.Direct3D.D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                null,
                Windows.Win32.Graphics.Direct3D11.D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                null,
                11,
                &d,
                null,
                null
                );
            Windows.Win32.PInvoke.CreateDirect3D11DeviceFromDXGIDevice(null, a.ObjRef);
        }

        IInspectable a;

        IDirect3DDevice canvas;

        using var pool =
            Direct3D11CaptureFramePool.Create(
                canvas,
                Windows.Graphics.DirectX.DirectXPixelFormat.R8G8B8A8UIntNormalized,
                2,
                item.Size
            );

        pool.FrameArrived += (pool, obj) => {
            using var frame = pool.TryGetNextFrame();
            //frame.Surface;
            Logger.LogInformation("[window] FrameArrived");

        };

        using var session = pool.CreateCaptureSession(item);
        session.StartCapture();
        Logger.LogInformation("[window] StartCapture");
        */
