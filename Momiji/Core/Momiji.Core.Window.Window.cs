using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Log;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal class NativeWindow : IWindow
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly WindowManager _windowManager;

    private readonly IWindowManager.OnPreCloseWindow? _onPreCloseWindow;
    private readonly IWindowManager.OnPostPaint? _onPostPaint;

    internal nint _hWindow;
    public nint Handle => _hWindow;

    private readonly ConcurrentDictionary<nint, (nint, PinnedDelegate<User32.WNDPROC>)> _oldWndProcMap = new();
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

        _logger.LogInformation("Create end");
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

            _logger.LogWithThreadId(LogLevel.Trace, "CreateWindowEx", Environment.CurrentManagedThreadId);
            var hWindow =
                User32.CreateWindowExW(
                    0,
                    windowClass.ClassName,
                    nint.Zero,
                    style,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    nint.Zero,
                    nint.Zero,
                    windowClass.HInstance,
                    new nint(thisHashCode)
                );
            var error = Marshal.GetLastPInvokeError();
            _logger.LogWithHWndAndErrorId(LogLevel.Information, "CreateWindowEx result", hWindow, error);
            if (hWindow == nint.Zero)
            {
                hWindow = default;
                throw new WindowException($"CreateWindowEx failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }

            return hWindow;
        });
    }

    public bool Close()
    {
        _logger.LogInformation($"Close {_hWindow:X}");
        return SendMessage(
            0x0010, //WM_CLOSE
            nint.Zero,
            nint.Zero
        );
    }

    private bool SendMessage(
        uint nMsg,
        nint wParam,
        nint lParam
    )
    {
        _logger.LogMsgWithThreadId(LogLevel.Trace, "SendMessageW", _hWindow, nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
        var _ =
            User32.SendMessageW(
                _hWindow,
                nMsg,
                wParam,
                lParam
            );
        var error = Marshal.GetLastPInvokeError();
        if (error != 0)
        {
            //UIPIに引っかかると5が返ってくる
            _logger.LogWithHWndAndErrorId(LogLevel.Error, "SendMessageW", _hWindow, error);
            return false;
        }
        return true;
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
            _logger.LogInformation($"MoveWindow {_hWindow:X} {x} {y} {width} {height} {repaint} current {Environment.CurrentManagedThreadId:X}");
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
                _logger.LogWithHWndAndErrorId(LogLevel.Error, "MoveWindow", _hWindow, Marshal.GetLastPInvokeError());
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
            _logger.LogInformation($"ShowWindow {_hWindow:X} {cmdShow} current {Environment.CurrentManagedThreadId:X}");
            var result =
                User32.ShowWindow(
                    _hWindow,
                    cmdShow
                );

            var error = Marshal.GetLastPInvokeError();

            //result=0: 実行前は非表示だった/ <>0:実行前から表示されていた
            _logger.LogInformation($"ShowWindow {_hWindow:X} {result} {error}");

            if (error == 1400) // ERROR_INVALID_WINDOW_HANDLE
            {
                throw new WindowException($"ShowWindow failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
            }

            {
                var wndpl = new User32.WINDOWPLACEMENT()
                {
                    length = Marshal.SizeOf<User32.WINDOWPLACEMENT>()
                };
                User32.GetWindowPlacement(_hWindow, ref wndpl);

                _logger.LogInformation($"GetWindowPlacement result cmdShow:{cmdShow} -> wndpl:{wndpl}");
            }

            return result;
        });
    }

    public bool SetWindowStyle(
        int style
    )
    {
        return Dispatch(() =>
        {
            var clientRect = new User32.RECT();

            {
                var result = User32.GetClientRect(_hWindow, ref clientRect);
                if (!result)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "GetClientRect failed", _hWindow, Marshal.GetLastPInvokeError());
                    return false;
                }
            }

            {
                var result = User32.AdjustWindowRect(ref clientRect, style, false);
                if (!result)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "AdjustWindowRect failed", _hWindow, Marshal.GetLastPInvokeError());
                    return false;
                }
            }

            {
                var (result, error) = SetWindowLong(-16, new nint(style)); //GWL_STYLE
                if (result == nint.Zero && error != 0)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "SetWindowLong failed", _hWindow, error);
                    return false;
                }
            }

            {
                var width = clientRect.right - clientRect.left;
                var height = clientRect.bottom - clientRect.top;

                _logger.LogInformation($"SetWindowPos {_hWindow:X} current {Environment.CurrentManagedThreadId:X}");
                var result =
                    User32.SetWindowPos(
                            _hWindow,
                            nint.Zero,
                            0,
                            0,
                            width,
                            height,
                            0x0002 //SWP_NOMOVE
                            //| 0x0001 //SWP_NOSIZE
                            | 0x0004 //SWP_NOZORDER
                            | 0x0020 //SWP_FRAMECHANGED
                        );

                if (!result)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "SetWindowPos failed", _hWindow, Marshal.GetLastPInvokeError());
                    return false;
                }
            }

            return true;
        });
    }

    private (nint, int) SetWindowLong(
        int nIndex,
        nint dwNewLong
    )
    {
        Marshal.SetLastPInvokeError(0);
        _logger.LogInformation($"SetWindowLong {_hWindow:X} {nIndex:X} {dwNewLong:X} current {Environment.CurrentManagedThreadId:X}");
        var result =
            Environment.Is64BitProcess
                ? User32.SetWindowLongPtrW(_hWindow, nIndex, dwNewLong)
                : User32.SetWindowLongW(_hWindow, nIndex, dwNewLong);
        var error = Marshal.GetLastPInvokeError();

        return (result, error);
    }


    internal nint WndProc(uint msg, nint wParam, nint lParam, out bool handled)
    {
        handled = false;
        _logger.LogMsgWithThreadId(LogLevel.Trace, "WndProc", _hWindow, msg, wParam, lParam, Environment.CurrentManagedThreadId);

        switch (msg)
        {
            case 0x0082://WM_NCDESTROY
                _logger.LogTrace("WM_NCDESTROY");
                _hWindow = default;
                break;

            case 0x0010://WM_CLOSE
                _logger.LogTrace("WM_CLOSE");
                try
                {
                    _onPreCloseWindow?.Invoke();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "onPreCloseWindow error");
                }
                /*
                _logger.LogTrace($"DestroyWindow {_hWindow:X} current {Environment.CurrentManagedThreadId:X}");
                var result = User32.DestroyWindow(_hWindow);
                if (!result)
                {
                    _logger.LogWithHWndAndErrorId(LogLevel.Error, "DestroyWindow", _hWindow, Marshal.GetLastPInvokeError());
                }
                handled = true;
                */
                break;

            case 0x0210://WM_PARENTNOTIFY
                _logger.LogTrace("WM_PARENTNOTIFY");
                switch (wParam & 0xFFFF)
                {
                    case 0x0001: //WM_CREATE
                        {
                            _logger.LogTrace($"WM_PARENTNOTIFY WM_CREATE {wParam:X}");
                            var childHWnd = lParam;
                            var isChildeWindowUnicode = (lParam != nint.Zero) && User32.IsWindowUnicode(childHWnd);
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
                            _logger.LogTrace($"WM_PARENTNOTIFY WM_DESTROY {wParam:X}");
                            var childHWnd = lParam;
                            if (_oldWndProcMap.TryRemove(childHWnd, out var pair))
                            {
                                var isChildeWindowUnicode = (lParam != nint.Zero) && User32.IsWindowUnicode(childHWnd);
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
        return nint.Zero;
    }

    private nint SubWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        _logger.LogMsgWithThreadId(LogLevel.Trace, "SubWndProc", hwnd, msg, wParam, lParam, Environment.CurrentManagedThreadId);

        var isWindowUnicode = (hwnd != nint.Zero) && User32.IsWindowUnicode(hwnd);
        nint result;

        if (_oldWndProcMap.TryGetValue(hwnd, out var pair))
        {
            _logger.LogMsgWithThreadId(LogLevel.Trace, "CallWindowProc", hwnd, msg, wParam, lParam, Environment.CurrentManagedThreadId);
            result = isWindowUnicode
                        ? User32.CallWindowProcW(pair.Item1, hwnd, msg, wParam, lParam)
                        : User32.CallWindowProcA(pair.Item1, hwnd, msg, wParam, lParam)
                        ;
        }
        else
        {
            _logger.LogWarning("unkown hwnd -> DefWindowProc");
            result = isWindowUnicode
                        ? User32.DefWindowProcW(hwnd, msg, wParam, lParam)
                        : User32.DefWindowProcA(hwnd, msg, wParam, lParam)
                        ;
        }

        switch (msg)
        {
            case 0x000F://WM_PAINT
                _logger.LogTrace("SubWndProc WM_PAINT");
                try
                {
                    _onPostPaint?.Invoke(hwnd);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "onPostPaint error");
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
