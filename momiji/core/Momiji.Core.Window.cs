using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
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
    public delegate void OnPostPaint(HandleRef hWindow);

    public IWindow CreateWindow(
        OnPreCloseWindow? onPreCloseWindow = default,
        OnPostPaint? onPostPaint = default
    );

    void CloseAll();
}

public interface IWindow
{
    HandleRef HandleRef { get; }
    void Close();
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

    private readonly PinnedDelegate<User32.WNDPROC> _wndProc;
    private readonly WindowClass _windowClass;

    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ConcurrentDictionary<IntPtr, NativeWindow> _windowMap = new();

    public delegate IntPtr OnWndProc(HandleRef hWindow, int msg, IntPtr wParam, IntPtr lParam);


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
        if (_disposed) return;

        if (disposing)
        {
            _logger.LogInformation($"[window manager] disposing");
            Cancel();

            _windowClass?.Dispose();
            _wndProc?.Dispose();
        }

        _disposed = true;
    }

    public void Cancel()
    {
        lock (_sync)
        {
            if (_processCancel == null)
            {
                _logger.LogInformation("[window manager] already stopped.");
                return;
            }

            try
            {
                _processCancel.Cancel();
                _processTask?.Wait();
            }
            catch (AggregateException e)
            {
                _logger.LogInformation(e, "[window manager] Process Cancel Exception");
            }
            finally
            {
                _processCancel.Dispose();
                _processCancel = null;

                _processTask?.Dispose();
                _processTask = null;
            }
            _logger.LogInformation("[window manager] stopped.");
        }
    }

    internal void Dispatch(Action item)
    {
        _logger.LogInformation("[window manager] Dispatch");
        if (_processTask == default)
        {
            throw new WindowException("message loop is not exists.");
        }
        
        _queue.Enqueue(item);
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
                _windowClass,
                onPreCloseWindow,
                onPostPaint
            );

        _windowMap.TryAdd(window.HandleRef.Handle, window);
        return window;
    }

    public void CloseAll()
    {
        Parallel.ForEach(_windowMap.Values, (window) => {
            window.Close();
        });
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
        await _processTask.ConfigureAwait(false);
    }

    private async Task Run()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);
        var thread = new Thread(() =>
        {
            try
            {
                MessageLoop();
                _logger.LogInformation($"[window manager] message loop normal end");
                tcs.SetResult();
            }
            catch (Exception e)
            {
                _logger.LogInformation(e, "[window manager] message loop exception");
                tcs.SetException(new WindowException("thread error", e));
            }
        })
        {
            IsBackground = false
        };

        thread.SetApartmentState(ApartmentState.STA);
        _logger.LogInformation($"[window manager] GetApartmentState {thread.GetApartmentState()}");
        thread.Start();

        await tcs.Task.ConfigureAwait(false);

        _logger.LogInformation($"[window manager] end");
    }

    private void MessageLoop()
    {
        if (_processCancel == null)
        {
            throw new InvalidOperationException($"{nameof(_processCancel)} is null.");
        }

        {
            var result = User32.IsGUIThread(true);
            _logger.LogInformation($"[window manager] IsGUIThread {result} {Marshal.GetLastWin32Error()}");
        }

        var msg = new User32.MSG();
        using var msgPin = new PinnedBuffer<User32.MSG>(msg);
        var forceCancel = false;
        var cancelled = false;

        var ct = _processCancel.Token;

        _logger.LogInformation($"[window manager] start message loop. (thread {Environment.CurrentManagedThreadId:X})");
        while (true)
        {
            //Logger.LogInformation($"[window] MessageLoop createThreadId {window.CreateThreadId:X} current {Thread.CurrentThread.ManagedThreadId:X}");
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

                // １秒以内にクローズされなければ、ループを終わらせる
                var _ =
                    Task.Delay(1000, CancellationToken.None)
                    .ContinueWith(
                        (task) => { forceCancel = true; },
                        TaskScheduler.Default
                    );
            }

            //ディスパッチ
            while (_queue.TryDequeue(out var result))
            {
                result.Invoke();
            }

            {
                //Logger.LogInformation($"[window] PeekMessage current {Thread.CurrentThread.ManagedThreadId:X}");
                if (!User32.PeekMessageW(
                        ref msg,
                        IntPtr.Zero,
                        0,
                        0,
                        0 // NOREMOVE
                ))
                {
                    var res =
                        User32.MsgWaitForMultipleObjects(
                            0,
                            IntPtr.Zero,
                            false,
                            1000,
                            0x04FF //QS_ALLINPUT
                        );
                    if (res == 258) // WAIT_TIMEOUT
                    {
                        //Logger.LogError($"[window] MsgWaitForMultipleObjects timeout.");
                        continue;
                    }
                    else if (res == 0) // WAIT_OBJECT_0
                    {
                        //Logger.LogError($"[window] MsgWaitForMultipleObjects have message.");
                        continue;
                    }

                    throw new WindowException($"MsgWaitForMultipleObjects failed {Marshal.GetLastWin32Error()}");
                }
            }
            //Logger.LogInformation($"[window] MSG {msg.hwnd:X} {msg.message:X} {msg.wParam:X} {msg.lParam:X} {msg.time} {msg.pt_x} {msg.pt_y}");

            var IsWindowUnicode = (msg.hwnd != IntPtr.Zero) && User32.IsWindowUnicode(new HandleRef(this, msg.hwnd));
            //Logger.LogInformation($"[window] IsWindowUnicode {IsWindowUnicode}");

            {
                //_logger.LogInformation("[window manager] GetMessage");
                var ret = IsWindowUnicode
                            ? User32.GetMessageW(ref msg, IntPtr.Zero, 0, 0)
                            : User32.GetMessageA(ref msg, IntPtr.Zero, 0, 0)
                            ;
                //_logger.LogInformation($"[window manager] GetMessage {ret} {msg.hwnd:X} {msg.message:X} {msg.wParam:X} {msg.lParam:X} {msg.time} {msg.pt_x} {msg.pt_y}");

                if (ret == -1)
                {
                    _logger.LogError($"[window manager] GetMessage failed {Marshal.GetLastWin32Error()}");
                    break;
                }
                else if (ret == 0)
                {
                    //TODO これを終了条件にはしない
                    //TODO メッセージQueueが捨てられるタイミング？？？
                    _logger.LogInformation($"[window manager] GetMessage Quit {msg.message:X}");
                    break;
                }
            }

            {
                //_logger.LogInformation("[window manager] TranslateMessage");
                var ret = User32.TranslateMessage(ref msg);
                //_logger.LogInformation($"[window manager] TranslateMessage {ret} {Marshal.GetLastWin32Error()}");
            }

            {
                //_logger.LogInformation("[window manager] DispatchMessage");
                var ret = IsWindowUnicode
                            ? User32.DispatchMessageW(ref msg)
                            : User32.DispatchMessageA(ref msg)
                            ;
                //_logger.LogInformation($"[window manager] DispatchMessage {ret} {Marshal.GetLastWin32Error()}");
            }
        }
        _logger.LogInformation("[window manager] end message loop.");
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var handleRef = new HandleRef(this, hwnd);
        var isWindowUnicode = (hwnd != IntPtr.Zero) && User32.IsWindowUnicode(handleRef);
        //_logger.LogInformation($"[window manager] WndProc[{hwnd:X} {msg:X} {wParam:X} {lParam:X}] current {Environment.CurrentManagedThreadId:X}");

        if (_windowMap.TryGetValue(handleRef.Handle, out var window))
        {
            switch (msg)
            {
                case 0x0010://WM_CLOSE
                    _windowMap.TryRemove(handleRef.Handle, out var _);
                    _logger.LogInformation($"[window manager] remove [{hwnd:X}]");
                    break;
            }

            //ウインドウに流す
            var result = window.WndProc(msg, wParam, lParam, out var handled);
            if (handled)
            {
                return result;
            }
        }

        return isWindowUnicode
            ? User32.DefWindowProcW(handleRef, msg, wParam, lParam)
            : User32.DefWindowProcA(handleRef, msg, wParam, lParam)
            ;
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
        _logger.LogInformation($"[window class] RegisterClass {_windowClass.lpszClassName} {atom} {Marshal.GetLastWin32Error()}");
        if (atom == 0)
        {
            throw new WindowException($"RegisterClass failed [{Marshal.GetLastWin32Error()}]");
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
        if (_disposed) return;

        if (disposing)
        {
            _logger.LogInformation($"[window class] disposing");
        }

        var result = User32.UnregisterClassW(_windowClass.lpszClassName, _windowClass.hInstance);
        _logger.LogInformation($"[window class] UnregisterClass {_windowClass.lpszClassName} {result} {Marshal.GetLastWin32Error()}");

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

    private readonly HandleRef _hWindow;
    public HandleRef HandleRef => _hWindow;

    private readonly ConcurrentDictionary<IntPtr, (IntPtr, PinnedDelegate<User32.WNDPROC>)> _oldWndProcMap = new();
    internal NativeWindow(
        ILoggerFactory loggerFactory,
        WindowManager windowManager,
        WindowClass windowClass,
        IWindowManager.OnPreCloseWindow? onPreCloseWindow = default,
        IWindowManager.OnPostPaint? onPostPaint = default
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<NativeWindow>();
        _windowManager = windowManager;

        _onPreCloseWindow = onPreCloseWindow;
        _onPostPaint = onPostPaint;

        var tcs = new TaskCompletionSource<HandleRef>(TaskCreationOptions.AttachedToParent);
        _windowManager.Dispatch(() => {
            try
            {
                var style = unchecked((int)
                    0x80000000 //WS_POPUP
                               // 0x00000000L //WS_OVERLAPPED
                               // | 0x00C00000L //WS_CAPTION
                               // | 0x00080000L //WS_SYSMENU
                               // | 0x00040000L //WS_THICKFRAME
                               //| 0x10000000L //WS_VISIBLE
                    );

                var CW_USEDEFAULT = unchecked((int)0x80000000);

                var hWindow = new HandleRef(this,
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
                        IntPtr.Zero
                    ));
                _logger.LogInformation($"[window] CreateWindowEx {hWindow.Handle:X} {Marshal.GetLastWin32Error()}");
                if (hWindow.Handle == IntPtr.Zero)
                {
                    hWindow = default;
                    throw new WindowException("CreateWindowEx failed");
                }

                tcs.SetResult(hWindow);
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        tcs.Task.Wait();
        _hWindow = tcs.Task.Result;
    }

    public void Close()
    {
        _logger.LogInformation($"[window] Close {_hWindow.Handle:X}");

        var IsWindowUnicode = User32.IsWindowUnicode(_hWindow);
        var _ = IsWindowUnicode
                    ? User32.SendNotifyMessageW(_hWindow, 0x0010, IntPtr.Zero, IntPtr.Zero)
                    : User32.SendNotifyMessageA(_hWindow, 0x0010, IntPtr.Zero, IntPtr.Zero)
                    ;
    }

    public bool Move(
        int x,
        int y,
        int width,
        int height,
        bool repaint
    )
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.AttachedToParent);
        _windowManager.Dispatch(() =>
        {
            _logger.LogInformation($"[window] MoveWindow {_hWindow.Handle:X} {x} {y} {width} {height} {repaint} (thread {Environment.CurrentManagedThreadId:X})");
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
                _logger.LogInformation($"[window] MoveWindow {_hWindow.Handle:X} {Marshal.GetLastWin32Error()}");
            }
            tcs.SetResult(result);
        });
        tcs.Task.Wait();
        return tcs.Task.Result;
    }

    public bool Show(
        int cmdShow
    )
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.AttachedToParent);
        _windowManager.Dispatch(() =>
        {
            _logger.LogInformation($"[window] ShowWindow {_hWindow.Handle:X} {cmdShow} (thread {Environment.CurrentManagedThreadId:X})");
            var result =
                User32.ShowWindow(
                    _hWindow,
                    cmdShow
                );

            if (!result)
            {
                _logger.LogInformation($"[window] ShowWindow {_hWindow.Handle:X} {Marshal.GetLastWin32Error()}");
            }
            tcs.SetResult(result);
        });
        tcs.Task.Wait();
        return tcs.Task.Result;
    }
    internal IntPtr WndProc(uint msg, IntPtr wParam, IntPtr lParam, out bool handled)
    {
        handled = false;
        //_logger.LogInformation($"[window] WndProc[{hwnd:X} {msg:X} {wParam:X} {lParam:X} current {Environment.CurrentManagedThreadId:X}");

        switch (msg)
        {
            //case 0x0082://WM_NCDESTROY
            //    _hWindow = default;
            //    User32.PostQuitMessage(0);
            //    handled = true;
            //    return IntPtr.Zero;

            //                case 0x0005://WM_SIZE
            //                  return IntPtr.Zero;

            case 0x0010://WM_CLOSE
                try
                {
                    _onPreCloseWindow?.Invoke();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "[window] onPreCloseWindow error");
                }
                //DefWindowProcに移譲
                break;

            case 0x0210://WM_PARENTNOTIFY
                //Logger.LogInformation($"[window] WM_PARENTNOTIFY [{wParam:X} {lParam:X}]");
                switch ((int)wParam & 0xFFFF)
                {
                    case 0x0001: //WM_CREATE
                        {
                            var childHWnd = new HandleRef(this, lParam);
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
                            _oldWndProcMap.TryAdd(childHWnd.Handle, (oldWndProc, subWndProc));

                            break;
                        }
                    case 0x0002: //WM_DESTROY
                        {
                            var childHWnd = new HandleRef(this, lParam);
                            if (_oldWndProcMap.TryRemove(childHWnd.Handle, out var pair))
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
        //        Logger.LogInformation($"[window] SubWndProc[{hwnd:X} {msg:X} {wParam:X} {lParam:X} current {Thread.CurrentThread.ManagedThreadId:X}");

        var handleRef = new HandleRef(this, hwnd);
        var isWindowUnicode = (hwnd != IntPtr.Zero) && User32.IsWindowUnicode(handleRef);
        IntPtr result;

        if (_oldWndProcMap.TryGetValue(hwnd, out var pair))
        {
            result = isWindowUnicode
                        ? User32.CallWindowProcW(pair.Item1, handleRef, msg, wParam, lParam)
                        : User32.CallWindowProcA(pair.Item1, handleRef, msg, wParam, lParam)
                        ;
        }
        else
        {
            result = isWindowUnicode
                        ? User32.DefWindowProcW(handleRef, msg, wParam, lParam)
                        : User32.DefWindowProcA(handleRef, msg, wParam, lParam)
                        ;
        }

        switch (msg)
        {
            case 0x000F://WM_PAINT
                        //                Logger.LogInformation($"[window] SubWndProc WM_PAINT[{hwnd:X} {msg:X} {wParam:X} {lParam:X} current {Thread.CurrentThread.ManagedThreadId:X}");
                try
                {
                    _onPostPaint?.Invoke(new HandleRef(this, hwnd));
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
