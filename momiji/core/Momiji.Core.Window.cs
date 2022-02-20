using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Gdi32 = Momiji.Interop.Gdi32.NativeMethods;
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

internal class WindowClass : IDisposable
{
    private ILoggerFactory LoggerFactory { get; }
    private ILogger Logger { get; }

    private bool disposed;

    private User32.WNDCLASS windowClass;

    internal IntPtr ClassName { get { return windowClass.lpszClassName; } }

    internal IntPtr HInstance { get { return windowClass.hInstance; } }

    internal WindowClass(
        ILoggerFactory loggerFactory,
        PinnedDelegate<User32.WNDPROC> wndProc,
        User32.WNDCLASS.CS cs = User32.WNDCLASS.CS.NONE
    )
    {
        LoggerFactory = loggerFactory;
        Logger = LoggerFactory.CreateLogger<WindowClass>();

        windowClass = new User32.WNDCLASS
        {
            style = cs,
            lpfnWndProc = wndProc.FunctionPointer,
            hInstance = Kernel32.GetModuleHandle(default),
            lpszClassName = Marshal.StringToHGlobalUni(nameof(NativeWindow) + Guid.NewGuid().ToString())
        };

        var atom = User32.RegisterClassW(ref windowClass);
        Logger.LogInformation($"[window class] RegisterClass {windowClass.lpszClassName} {atom} {Marshal.GetLastWin32Error()}");
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
        if (disposed) return;

        if (disposing)
        {
            Logger.LogInformation($"[window class] disposing");
        }

        var result = User32.UnregisterClassW(windowClass.lpszClassName, windowClass.hInstance);
        Logger.LogInformation($"[window class] UnregisterClass {windowClass.lpszClassName} {result} {Marshal.GetLastWin32Error()}");

        Marshal.FreeHGlobal(windowClass.lpszClassName);

        disposed = true;
    }
}

public class NativeWindow
{
    private ILoggerFactory LoggerFactory { get; }
    private ILogger Logger { get; }

    public delegate void OnCreateWindow(HandleRef hWindow, ref int width, ref int height);
    public delegate void OnPreCloseWindow();
    public delegate void OnPostPaint(HandleRef hWindow);

    private readonly OnCreateWindow? onCreateWindow;
    private readonly OnPreCloseWindow? onPreCloseWindow;
    private readonly OnPostPaint? onPostPaint;

    private HandleRef hWindow;

    private readonly ConcurrentDictionary<IntPtr, (IntPtr, PinnedDelegate<User32.WNDPROC>)> oldWndProcMap = new();

    public NativeWindow(
        ILoggerFactory loggerFactory,
        OnCreateWindow? onCreateWindow = default,
        OnPreCloseWindow? onPreCloseWindow = default,
        OnPostPaint? onPostPaint = default
    )
    {
        LoggerFactory = loggerFactory;
        Logger = LoggerFactory.CreateLogger<NativeWindow>();

        this.onCreateWindow = onCreateWindow;
        this.onPreCloseWindow = onPreCloseWindow;
        this.onPostPaint = onPostPaint;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var wndProc = new PinnedDelegate<User32.WNDPROC>(new(WndProc));
        using var windowClass = new WindowClass(LoggerFactory, wndProc);

        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);

        //TODO タスク化できないか？
        var thread = new Thread(() =>
        {
            try
            {
                WindowThread(windowClass, cancellationToken);
                tcs.SetResult();
                Logger.LogInformation($"[window] normal end");
            }
#pragma warning disable CA1031 // 一般的な例外の種類はキャッチしません
            catch (Exception e)
#pragma warning restore CA1031 // 一般的な例外の種類はキャッチしません
            {
                tcs.SetException(new WindowException("thread error", e));
                Logger.LogInformation(e, "[window] exception");
            }
        })
        {
            IsBackground = false
        };

        thread.SetApartmentState(ApartmentState.STA);
        Logger.LogInformation($"[window] GetApartmentState {thread.GetApartmentState()}");
        thread.Start();

        await tcs.Task.ConfigureAwait(false);
        //thread.Join();

        Logger.LogInformation($"[window] end");
    }

    private void WindowThread(WindowClass windowClass, CancellationToken cancellationToken)
    {
        {
            var result = User32.IsGUIThread(true);
            Logger.LogInformation($"[window] IsGUIThread {result} {Marshal.GetLastWin32Error()}");
        }
        var style = unchecked((int)
            0x80000000 //WS_POPUP
                        // 0x00000000L //WS_OVERLAPPED
                        // | 0x00C00000L //WS_CAPTION
                        // | 0x00080000L //WS_SYSMENU
                        // | 0x00040000L //WS_THICKFRAME
                        //| 0x10000000L //WS_VISIBLE
            );

        var CW_USEDEFAULT = unchecked((int)0x80000000);

        hWindow = new HandleRef(this,
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
        Logger.LogInformation($"[window] CreateWindowEx {hWindow.Handle:X} {Marshal.GetLastWin32Error()}");
        if (hWindow.Handle == IntPtr.Zero)
        {
            throw new WindowException("CreateWindowEx failed");
        }

        //RECTを受け取る
        int width = 100;
        int height = 100;
        onCreateWindow?.Invoke(hWindow, ref width, ref height);

        User32.MoveWindow(
            hWindow,
            0,
            0,
            width, 
            height, 
            true
        );

        User32.ShowWindow(
            hWindow, 
            5 // SW_SHOW
        );

        MessageLoop(cancellationToken);

        hWindow = default;
    }

    private void MessageLoop(CancellationToken cancellationToken)
    {
        /*
        //表示していないとwinrt::hresult_invalid_argumentになる
        var item = GraphicsCaptureItemInterop.CreateForWindow(hWindow);
        item.Closed += (item, obj) => {
            Logger.LogInformation("[window] GraphicsCaptureItem closed");
        };

        using var canvas = new CanvasDevice();

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

        var msg = new User32.MSG();
        using var msgPin = new PinnedBuffer<User32.MSG>(msg);
        var forceCancel = false;

        while (true)
        {
            //Logger.LogInformation($"[window] MessageLoop createThreadId {window.CreateThreadId:X} current {Thread.CurrentThread.ManagedThreadId:X}");
            if (forceCancel)
            {
                Logger.LogInformation("[window] force canceled");
                break;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogInformation("[window] canceled");
                Close();

                // １秒以内にクローズされなければ、ループを終わらせる
                var _ = 
                    Task.Delay(1000, CancellationToken.None)
                    .ContinueWith(
                        (task) => { forceCancel = true; },
                        TaskScheduler.Default
                    );
            }

            if (hWindow.Handle == IntPtr.Zero)
            {
                Logger.LogInformation("[window] destroyed");
                break;
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
                //Logger.LogInformation("[window] GetMessage");
                var ret = IsWindowUnicode
                            ? User32.GetMessageW(ref msg, IntPtr.Zero, 0, 0)
                            : User32.GetMessageA(ref msg, IntPtr.Zero, 0, 0)
                            ;
                //Logger.LogInformation($"[window] GetMessage {ret} {msg.hwnd:X} {msg.message:X} {msg.wParam:X} {msg.lParam:X} {msg.time} {msg.pt_x} {msg.pt_y}");

                if (ret == -1)
                {
                    Logger.LogError($"[window] GetMessage failed {Marshal.GetLastWin32Error()}");
                    break;
                }
                else if (ret == 0)
                {
                    Logger.LogInformation($"[window] GetMessage Quit {msg.message:X}");
                    break;
                }
            }

            {
                //    Logger.LogInformation("[window] TranslateMessage");
                var _ = User32.TranslateMessage(ref msg);
                //    Logger.LogInformation($"[window] TranslateMessage {ret} {Marshal.GetLastWin32Error()}");
            }

            {
                //    Logger.LogInformation("[window] DispatchMessage");
                var _ = IsWindowUnicode
                            ? User32.DispatchMessageW(ref msg)
                            : User32.DispatchMessageA(ref msg)
                            ;
                //    Logger.LogInformation($"[window] DispatchMessage {ret} {Marshal.GetLastWin32Error()}");
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var handleRef = new HandleRef(this, hwnd);
        var isWindowUnicode = (lParam != IntPtr.Zero) && User32.IsWindowUnicode(handleRef);
        Logger.LogInformation($"[window] WndProc[{hwnd:X} {msg:X} {wParam:X} {lParam:X} current {Environment.CurrentManagedThreadId:X}");

        switch (msg)
        {
            case 0x0082://WM_NCDESTROY
                hWindow = default;
                User32.PostQuitMessage(0);
                return IntPtr.Zero;

//                case 0x0005://WM_SIZE
//                  return IntPtr.Zero;

            case 0x0010://WM_CLOSE
                try
                {
                    onPreCloseWindow?.Invoke();
                }
#pragma warning disable CA1031 // 一般的な例外の種類はキャッチしません
                catch (Exception e)
#pragma warning restore CA1031 // 一般的な例外の種類はキャッチしません
                {
                    Logger.LogError(e, "[window] onPreCloseWindow error");
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
                            oldWndProcMap.TryAdd(childHWnd.Handle, (oldWndProc, subWndProc));

                            break;
                        }
                    case 0x0002: //WM_DESTROY
                        {
                            var childHWnd = new HandleRef(this, lParam);
                            if (oldWndProcMap.TryRemove(childHWnd.Handle, out var pair))
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
        return isWindowUnicode
            ? User32.DefWindowProcW(handleRef, msg, wParam, lParam)
            : User32.DefWindowProcA(handleRef, msg, wParam, lParam)
            ;
    }

    private IntPtr SubWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        //Logger.LogInformation($"[window] SubWndProc[{hwnd:X} {msg:X} {wParam:X} {lParam:X} current {Thread.CurrentThread.ManagedThreadId:X}");

        var handleRef = new HandleRef(this, hwnd);
        var isWindowUnicode = (lParam != IntPtr.Zero) && User32.IsWindowUnicode(handleRef);
        IntPtr result;

        if (oldWndProcMap.TryGetValue(hwnd, out var pair))
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

/*            switch (msg)
        {
            case 0x000F://WM_PAINT
                //Logger.LogInformation($"[window] SubWndProc WM_PAINT[{hwnd:X} {msg:X} {wParam:X} {lParam:X} current {Thread.CurrentThread.ManagedThreadId:X}");
                try
                {
                    onPostPaint?.Invoke(new HandleRef(this, hwnd));
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "[window] onPostPaint error");
                }
                break;

            default:
                break;
        }
*/
        try
        {
            onPostPaint?.Invoke(new HandleRef(this, hwnd));
        }
#pragma warning disable CA1031 // 一般的な例外の種類はキャッチしません
        catch (Exception e)
#pragma warning restore CA1031 // 一般的な例外の種類はキャッチしません
        {
            Logger.LogError(e, "[window] onPostPaint error");
        }

        return result;
    }

    private void Capture1(IntPtr hwnd)
    {
        var hWindow = new HandleRef(this, hwnd);

        var rcClient = new User32.RECT();
        {
            var res = User32.GetClientRect(hWindow, ref rcClient);
            if (!res)
            {
                Logger.LogError($"[window] GetClientRect failed {Marshal.GetLastWin32Error()}");
                return;
            }
        }

        var cx = (int)(rcClient.right - rcClient.left);
        var cy = (int)(rcClient.bottom - rcClient.top);

        var hdcWindow = new HandleRef(this, User32.GetDC(hWindow));
        if (hdcWindow.Handle == default)
        {
            Logger.LogError($"[window] GetDC failed {Marshal.GetLastWin32Error()}");
            return;
        }
        try
        {
            var hdcMemDC = new HandleRef(this, Gdi32.CreateCompatibleDC(hdcWindow));
            if (hdcMemDC.Handle == default)
            {
                Logger.LogError($"[window] CreateCompatibleDC failed {Marshal.GetLastWin32Error()}");
                return;
            }
            try
            {
                var hbmScreen = new HandleRef(this,
                    Gdi32.CreateCompatibleBitmap(
                        hdcWindow,
                        cx,
                        cy
                    ));
                if (hbmScreen.Handle == default)
                {
                    Logger.LogError($"[window] CreateCompatibleBitmap failed {Marshal.GetLastWin32Error()}");
                    return;
                }
                try
                {
                    var hOld = Gdi32.SelectObject(hdcMemDC, hbmScreen);
                    if (hOld == default)
                    {
                        Logger.LogError($"[window] SelectObject failed {Marshal.GetLastWin32Error()}");
                        return;
                    }

                    {
                        var res =
                            Gdi32.BitBlt(
                                hdcMemDC,
                                0,
                                0,
                                cx,
                                cy,
                                hdcWindow,
                                0,
                                0,
                                0x00CC0020 /*SRCCOPY*/);
                        if (!res)
                        {
                            Logger.LogError($"[window] BitBlt failed {Marshal.GetLastWin32Error()}");
                        }
                    }

                    //TODO hbmScreenを転記する
                }
                finally
                {
                    var res = Gdi32.DeleteObject(hbmScreen);
                    if (!res)
                    {
                        Logger.LogError($"[window] DeleteObject failed {Marshal.GetLastWin32Error()}");
                    }
                }
            }
            finally
            {
                var res = Gdi32.DeleteDC(hdcMemDC);
                if (!res)
                {
                    Logger.LogError($"[window] DeleteDC failed {Marshal.GetLastWin32Error()}");
                }
            }
        }
        finally
        {
            var res = User32.ReleaseDC(hWindow, hdcWindow);
            if (res != 1)
            {
                Logger.LogError($"[window] ReleaseDC failed {Marshal.GetLastWin32Error()}");
            }
        }
    }

    public void Close()
    {
        Logger.LogInformation($"[window] Close {hWindow.Handle:X}");

        var IsWindowUnicode = User32.IsWindowUnicode(hWindow);
        var _ = IsWindowUnicode
                    ? User32.SendNotifyMessageW(hWindow, 0x0010, IntPtr.Zero, IntPtr.Zero)
                    : User32.SendNotifyMessageA(hWindow, 0x0010, IntPtr.Zero, IntPtr.Zero)
                    ;
    }
}
