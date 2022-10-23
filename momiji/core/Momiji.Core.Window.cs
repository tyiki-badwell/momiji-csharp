using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Interop.User32;
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

    //private readonly HWindowStation? _windowStation;
    //private readonly HDesktop? _desktop;

    public WindowManager(
        ILoggerFactory loggerFactory
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowManager>();

        /*
        _windowStation =
            User32.CreateWindowStationW(
                null,
                User32.CWF.NONE,
                User32.ACCESS_MASK.GENERIC_ALL,
                IntPtr.Zero
            );
        if (_windowStation.IsInvalid)
        {
            throw new WindowException($"CreateWindowStationW failed {Marshal.GetLastWin32Error()}");
        }

        {
            using var winSta = User32.GetProcessWindowStation();
            _logger.LogInformation($"[window manager] ProcessWindowStation now:{winSta.DangerousGetHandle():X} new:{_windowStation.DangerousGetHandle():X}");
        }

        if (!_windowStation.SetProcessWindowStation())
        {
            throw new WindowException($"[window manager] failed SetProcessWindowStation {Marshal.GetLastWin32Error()}");
        }
        */

        /*
        _desktop = 
            User32.CreateDesktopW(
                "test", 
                IntPtr.Zero, 
                IntPtr.Zero, 
                User32.DF.NONE, 
                User32.ACCESS_MASK.GENERIC_ALL, 
                IntPtr.Zero
            );
        if (_desktop.IsInvalid)
        {
            throw new WindowException($"CreateDesktopW failed {Marshal.GetLastWin32Error()}");
        }

        {
            using var desktop = User32.GetThreadDesktop(Environment.CurrentManagedThreadId);
            _logger.LogInformation($"[window manager] ThreadDesktop now:{desktop.DangerousGetHandle():X} new:{_desktop.DangerousGetHandle():X}");
        }
        */

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
            _logger.LogInformation($"[window manager] disposing");
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

        lock (_sync)
        {
            if (processCancel == null)
            {
                _logger.LogInformation("[window manager] already stopped.");
                return;
            }
            _processCancel = default;
        }

        var task = _processTask;

        processCancel.Cancel();

        try
        {
            task?.Wait();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "[window manager] cancel failed.");
        }
    }

    internal T Dispatch<T>(Func<T> item)
    {
        if (_uiThreadId == Environment.CurrentManagedThreadId)
        {
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
        _logger.LogInformation($"[window manager] Dispatch {Environment.CurrentManagedThreadId:X}");
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
                _windowClass,
                onPreCloseWindow,
                onPostPaint
            );

        _windowMap.TryAdd(window.HandleRef.Handle, window);
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
                _logger.LogInformation(task.Exception, $"[window manager] task end");

                _processTask = default;

                _processCancel?.Dispose();
                _processCancel = default;

                _logger.LogInformation("[window manager] stopped.");

            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "[window manager] await failed.");
        }

        _logger.LogInformation($"[window manager] await end");
    }

    private async Task Run()
    {
        {
            using var desktop = User32.GetThreadDesktop(Kernel32.GetCurrentThreadId());
            _logger.LogInformation($"[window manager] GetThreadDesktop now:{desktop.DangerousGetHandle():X}");

            if (desktop.IsInvalid)
            {
                _logger.LogInformation($"GetThreadDesktop failed {Marshal.GetLastWin32Error()}");
            }
            else
            {
                PrintDesktopACL(desktop);
            }
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.AttachedToParent);
        var thread = new Thread(() =>
        {
            try
            {
                MessageLoop();
                _logger.LogInformation($"[window manager] message loop normal end");
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
        _logger.LogInformation($"[window manager] GetApartmentState {thread.GetApartmentState()}");
        _uiThreadId = thread.ManagedThreadId;
        thread.Start();

        await tcs.Task.ContinueWith((_) => {
            _logger.LogInformation($"[window manager] message loop task end");
            _uiThreadId = default;
        }).ConfigureAwait(false);

        var _ = tcs.Task.Result;

        if (_windowMap.Count > 0)
        {
            _logger.LogWarning($"[window manager] window left {_windowMap.Count}");

            //TODO メッセージループが止まった後でウインドウを破棄する方法？
        }

    }


    private void PrintDesktopACL(HDesktop desktop)
    {
        var result =
            User32.GetSecurityInfo(
                desktop.DangerousGetHandle(),
                User32.SE_OBJECT_TYPE.SE_WINDOW_OBJECT,
                User32.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION
                | User32.SECURITY_INFORMATION.GROUP_SECURITY_INFORMATION
                | User32.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION
                //| User32.SECURITY_INFORMATION.SACL_SECURITY_INFORMATION
                ,
                out var owner,
                out var group,
                out var dacl,
                out var sacl,
                out var sd
            );
        if (result != 0)
        {
            throw new WindowException($"GetSecurityInfo failed {result}");
        }

        {
            User32.ConvertSidToStringSidW(owner, out var str);
            var sid = Marshal.PtrToStringUni(str);
            _logger.LogInformation($"[window manager] ThreadDesktop owner:{sid}");
            Marshal.FreeHGlobal(str);
        }
        {
            User32.ConvertSidToStringSidW(group, out var str);
            var sid = Marshal.PtrToStringUni(str);
            _logger.LogInformation($"[window manager] ThreadDesktop group:{sid}");
            Marshal.FreeHGlobal(str);
        }

        var trustee = new User32.Trustee()
        {
            pSid = owner,
            trusteeForm = 0,
            trusteeType = 1
        };

        User32.GetEffectiveRightsFromAclW(dacl, ref trustee, out var ar);
        _logger.LogInformation($"[window manager] ThreadDesktop access:{ar}");

        Marshal.FreeHGlobal(sd);
    }

    private void MessageLoop()
    {
        if (_processCancel == null)
        {
            throw new InvalidOperationException($"{nameof(_processCancel)} is null.");
        }

        {
            using var desktop = User32.GetThreadDesktop(Kernel32.GetCurrentThreadId());
            _logger.LogInformation($"[window manager] GetThreadDesktop now:{desktop.DangerousGetHandle():X}");

            if (desktop.IsInvalid)
            {
                _logger.LogInformation($"GetThreadDesktop failed {Marshal.GetLastWin32Error()}");
            }
            else
            {
                PrintDesktopACL(desktop);
            }
        }

        {
            using var desktop =
                User32.CreateDesktopW(
                    "test",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    User32.DF.NONE,
                    User32.DESKTOP_ACCESS_MASK.GENERIC_ALL,
                    IntPtr.Zero
                );
            if (desktop.IsInvalid)
            {
                _logger.LogInformation($"CreateDesktopW failed {Marshal.GetLastWin32Error()}");
            }
            else
            {
                PrintDesktopACL(desktop);
            }
        }
        //TODO HOOKが仕掛けられてたら解除する
        /*
        if (!_desktop.SetThreadDesktop())
        {
            throw new WindowException($"[window manager] SetThreadDesktop failed. {Marshal.GetLastWin32Error()}");
        }
        */
        {
            var result = User32.IsGUIThread(true);
            _logger.LogInformation($"[window manager] IsGUIThread {result} {Marshal.GetLastWin32Error()}");
        }

        var forceCancel = false;
        var cancelled = false;

        var ct = _processCancel.Token;

        using var waitHandlesPin = new PinnedBuffer<IntPtr[]>(new IntPtr[] {
            _queueEvent.WaitHandle.SafeWaitHandle.DangerousGetHandle(),
            ct.WaitHandle.SafeWaitHandle.DangerousGetHandle()
         });

        _logger.LogInformation($"[window manager] start message loop. current {Environment.CurrentManagedThreadId:X}");
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

            {
                //_logger.LogInformation($"[window manager] MsgWaitForMultipleObjectsEx current {Environment.CurrentManagedThreadId:X}");
                var res =
                    User32.MsgWaitForMultipleObjects/*Ex*/(
                        (uint)waitHandlesPin.Target.Length,
                        waitHandlesPin.AddrOfPinnedObject,
                        false,
                        1000,
                        0x04FF/*, //QS_ALLINPUT
                        0x0004*/ //MWMO_INPUTAVAILABLE
                    );
                if (res == 258) // WAIT_TIMEOUT
                {
                    //_logger.LogInformation($"[window manager] MsgWaitForMultipleObjectsEx timeout.");
                    continue;
                }
                else if (res == 0) // WAIT_OBJECT_0
                {
                    //_logger.LogInformation($"[window manager] MsgWaitForMultipleObjectsEx comes queue event.");
                    _queueEvent.Reset();
                    //ディスパッチ
                    while (_queue.TryDequeue(out var result))
                    {
                        //_logger.LogInformation($"[window manager] Invoke current {Environment.CurrentManagedThreadId:X}");
                        result.Invoke();
                    }
                    continue;
                }
                else if (res == 1) // WAIT_OBJECT_0+1
                {
                    //_logger.LogInformation($"[window manager] MsgWaitForMultipleObjectsEx comes cancel event.");
                    continue;
                }
                else if (res == 2) // WAIT_OBJECT_0+2
                {
                    //_logger.LogInformation($"[window manager] MsgWaitForMultipleObjectsEx comes message.");
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
        using var msgPin = new PinnedBuffer<User32.MSG>(msg);

        while (true)
        {
            //_logger.LogInformation($"[window manager] PeekMessage current {Environment.CurrentManagedThreadId:X}");
            if (!User32.PeekMessageW(
                    ref msg,
                    IntPtr.Zero,
                    0,
                    0,
                    0x0001 // PM_REMOVE
            ))
            {
                //_logger.LogInformation("[window manager] PeekMessage NONE");
                return;
            }
            //_logger.LogInformation($"[window manager] MSG {msg.hwnd:X} {msg.message:X} {msg.wParam:X} {msg.lParam:X} {msg.time} {msg.pt_x} {msg.pt_y}");

            var IsWindowUnicode = (msg.hwnd != IntPtr.Zero) && User32.IsWindowUnicode(new HandleRef(this, msg.hwnd));
            //Logger.LogInformation($"[window] IsWindowUnicode {IsWindowUnicode}");

            {
                var ret = User32.InSendMessageEx(IntPtr.Zero);
                //_logger.LogInformation($"[window manager] InSendMessageEx {ret:X}");
                if ((ret & (0x00000008 | 0x00000001)) == 0x00000001) //ISMEX_SEND
                {
                    //_logger.LogInformation("[window manager] ISMEX_SEND");
                    var _ = User32.ReplyMessage(new IntPtr(1));
                    //_logger.LogInformation($"[window manager] ReplyMessage {ret2} {Marshal.GetLastWin32Error()}");
                }
            }

            {
                //_logger.LogInformation("[window manager] TranslateMessage");
                var _ = User32.TranslateMessage(ref msg);
                //_logger.LogInformation($"[window manager] TranslateMessage {ret} {Marshal.GetLastWin32Error()}");
            }

            {
                //_logger.LogInformation("[window manager] DispatchMessage");
                var _ = IsWindowUnicode
                            ? User32.DispatchMessageW(ref msg)
                            : User32.DispatchMessageA(ref msg)
                            ;
                //_logger.LogInformation($"[window manager] DispatchMessage {ret} {Marshal.GetLastWin32Error()}");
            }
        }
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
                case 0x0082://WM_NCDESTROY
                    //_logger.LogInformation($"[window manager] WM_NCDESTROY[{hwnd:X} {msg:X} {wParam:X} {lParam:X} current {Environment.CurrentManagedThreadId:X}");
                    _windowMap.TryRemove(handleRef.Handle, out var _);
                    _logger.LogInformation($"[window manager] remove window map [{hwnd:X}]");
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
        if (_disposed)
        {
            return;
        }

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

    private HandleRef _hWindow;
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

        _hWindow = Dispatch(() => {
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
            _logger.LogInformation($"[window] CreateWindowEx {hWindow.Handle:X} {Marshal.GetLastWin32Error()} current {Environment.CurrentManagedThreadId:X}");
            if (hWindow.Handle == IntPtr.Zero)
            {
                hWindow = default;
                throw new WindowException("CreateWindowEx failed");
            }
            return hWindow;
        });

        _logger.LogInformation("[window] Create end");
    }

    public T Dispatch<T>(Func<T> item)
    {
        return _windowManager.Dispatch(item);
    }

    public bool Close()
    {
        _logger.LogInformation($"[window] Close {_hWindow.Handle:X}");
        return SendNotifyMessage(
            0x0112, //WM_SYSCOMMAND
            (IntPtr)0xF060, //SC_CLOSE
            IntPtr.Zero
        );
    }

    private bool SendNotifyMessage(
        int nMsg,
        IntPtr wParam,
        IntPtr lParam
    )
    {
        return Dispatch(() => 
        {
            _logger.LogInformation($"[window] SendNotifyMessageW {_hWindow.Handle:X} {nMsg:X} {wParam:X} {lParam:X} current {Environment.CurrentManagedThreadId:X}");
            var result =
                User32.SendNotifyMessageW(
                    _hWindow,
                    nMsg,
                    wParam,
                    lParam
                );

            if (!result)
            {
                _logger.LogError($"[window] SendNotifyMessageW {_hWindow.Handle:X} {Marshal.GetLastWin32Error()}");
            }
            return result;
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
            _logger.LogInformation($"[window] MoveWindow {_hWindow.Handle:X} {x} {y} {width} {height} {repaint} current {Environment.CurrentManagedThreadId:X}");
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
                _logger.LogError($"[window] MoveWindow {_hWindow.Handle:X} {Marshal.GetLastWin32Error()}");
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
            _logger.LogInformation($"[window] ShowWindow {_hWindow.Handle:X} {cmdShow} current {Environment.CurrentManagedThreadId:X}");
            var result =
                User32.ShowWindow(
                    _hWindow,
                    cmdShow
                );

            //result=0: 実行前は非表示だった/ <>0:実行前から表示されていた
            _logger.LogInformation($"[window] ShowWindow {_hWindow.Handle:X} {result} {Marshal.GetLastWin32Error()}");

            return result;
        });
    }
    internal IntPtr WndProc(uint msg, IntPtr wParam, IntPtr lParam, out bool handled)
    {
        handled = false;
        //_logger.LogInformation($"[window] WndProc[{hwnd:X} {msg:X} {wParam:X} {lParam:X} current {Environment.CurrentManagedThreadId:X}");

        switch (msg)
        {
            //case 0x0002://WM_DESTROY
            //    _logger.LogInformation($"[window] WM_DESTROY[{_hWindow:X} {msg:X} {wParam:X} {lParam:X} current {Environment.CurrentManagedThreadId:X}");
            //    break;

            case 0x0082://WM_NCDESTROY
                _logger.LogInformation($"[window] WM_NCDESTROY[{_hWindow:X} {msg:X} {wParam:X} {lParam:X} current {Environment.CurrentManagedThreadId:X}");
                _hWindow = default;
                break;

            case 0x0010://WM_CLOSE
                _logger.LogInformation($"[window] WM_CLOSE[{_hWindow:X} {msg:X} {wParam:X} {lParam:X} current {Environment.CurrentManagedThreadId:X}");
                try
                {
                    _onPreCloseWindow?.Invoke();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "[window] onPreCloseWindow error");
                }

                _logger.LogInformation($"[window] DestroyWindow {_hWindow.Handle:X} current {Environment.CurrentManagedThreadId:X}");
                var result = User32.DestroyWindow(_hWindow);
                if (!result)
                {
                    _logger.LogError($"[window] DestroyWindow {_hWindow.Handle:X} {Marshal.GetLastWin32Error()}");
                }

                handled = true;
                break;

            case 0x0210://WM_PARENTNOTIFY
                //Logger.LogInformation($"[window] WM_PARENTNOTIFY [{wParam:X} {lParam:X}]");
                switch ((int)wParam & 0xFFFF)
                {
                    case 0x0001: //WM_CREATE
                        {
                            _logger.LogInformation($"[window] WM_PARENTNOTIFY [{wParam:X}(WM_CREATE) {lParam:X}]");
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
                            _logger.LogInformation($"[window] WM_PARENTNOTIFY [{wParam:X}(WM_DESTROY) {lParam:X}]");
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
