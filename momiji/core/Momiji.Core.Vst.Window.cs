using Microsoft.Extensions.Logging;
using Momiji.Interop.Buffer;
using Momiji.Interop.Kernel32;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Momiji.Core.Vst
{
    public class VstWindowException : Exception
    {
        public VstWindowException()
        {
        }

        public VstWindowException(string message) : base(message)
        {
        }

        public VstWindowException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    internal class WindowClass : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        private bool disposed;

        private NativeMethods.WNDCLASS windowClass;

        internal IntPtr ClassName { get { return windowClass.lpszClassName; } }

        internal IntPtr HInstance { get { return windowClass.hInstance; } }

        public WindowClass(
            ILoggerFactory loggerFactory,
            PinnedDelegate<NativeMethods.WNDPROC> wndProc
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<WindowClass>();

            windowClass = new NativeMethods.WNDCLASS
            {
                //style = NativeMethods.WNDCLASS.CS.HREDRAW | NativeMethods.WNDCLASS.CS.VREDRAW,
                lpfnWndProc = wndProc.FunctionPointer,
                hInstance = NativeMethods.GetModuleHandle(null),
                lpszClassName = Marshal.StringToHGlobalUni(nameof(Window) + Guid.NewGuid().ToString())
            };

            var atom = NativeMethods.RegisterClass(ref windowClass);
            Logger.LogInformation($"[window class] RegisterClass {windowClass.lpszClassName} {atom} {Marshal.GetLastWin32Error()}");
            if (atom == 0)
            {
                throw new VstWindowException($"RegisterClass failed [{Marshal.GetLastWin32Error()}]");
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

            var result = NativeMethods.UnregisterClass(windowClass.lpszClassName, windowClass.hInstance);
            Logger.LogInformation($"[window class] UnregisterClass {windowClass.lpszClassName} {result} {Marshal.GetLastWin32Error()}");

            Marshal.FreeHGlobal(windowClass.lpszClassName);

            disposed = true;
        }
    }

    public class Window : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        private bool disposed;

        public delegate void OnCreateWindow(HandleRef hWnd, ref int width, ref int height);
        public delegate void OnPreCloseWindow();

        private readonly OnCreateWindow onCreateWindow;
        private readonly OnPreCloseWindow onPreCloseWindow;

        private HandleRef hWindow;
        private Bitmap bitmap;

        public Window(
            ILoggerFactory loggerFactory,
            OnCreateWindow onCreateWindow,
            OnPreCloseWindow onPreCloseWindow
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<Window>();

            this.onCreateWindow = onCreateWindow;
            this.onPreCloseWindow = onPreCloseWindow;
        }

        ~Window()
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
                Logger.LogInformation($"[window] disposing");

                bitmap?.Dispose();
                bitmap = default;
            }

            disposed = true;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using var wndProc = new PinnedDelegate<NativeMethods.WNDPROC>(new(WndProc));
            using var windowClass = new WindowClass(LoggerFactory, wndProc);

            var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);

            var thread = new Thread(() =>
            {
                try
                {
                    WindowThread(windowClass, cancellationToken);
                    tcs.SetResult();
                    Logger.LogInformation($"[vst window] normal");
                }
                catch (Exception e)
                {
                    tcs.SetException(new VstWindowException("thread error", e));
                    Logger.LogInformation(e, $"[vst window] exception");
                }
            })
            {
                IsBackground = false
            };

            thread.SetApartmentState(ApartmentState.STA);
            Logger.LogInformation($"[vst window] GetApartmentState {thread.GetApartmentState()}");
            thread.Start();

            await tcs.Task.ConfigureAwait(false);
            //thread.Join();

            Logger.LogInformation($"[vst window] end");
        }

        private void WindowThread(WindowClass windowClass, CancellationToken cancellationToken)
        {
            {
                var result = NativeMethods.IsGUIThread(true);
                Logger.LogInformation($"[vst window] IsGUIThread {result} {Marshal.GetLastWin32Error()}");
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
                NativeMethods.CreateWindowEx(
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
            Logger.LogInformation($"[window] CreateWindowEx {hWindow:X} {Marshal.GetLastWin32Error()}");
            if (hWindow.Handle == IntPtr.Zero)
            {
                throw new VstWindowException("CreateWindowEx failed");
            }

            //RECTを受け取る
            int width = 100;
            int height = 100;
            onCreateWindow?.Invoke(hWindow, ref width, ref height);

            bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);

            NativeMethods.MoveWindow(
                hWindow,
                0,
                0,
                width, 
                height, 
                true
            );
            NativeMethods.ShowWindow(hWindow, 5 /*SW_SHOW*/);

            MessageLoop(cancellationToken);

            hWindow = default;
        }

        private void MessageLoop(CancellationToken cancellationToken)
        {
            var msg = new NativeMethods.MSG();
            using var msgPin = new PinnedBuffer<NativeMethods.MSG>(msg);
            var forceCancel = false;

            while (true)
            {
                //Logger.LogInformation($"[vst window] MessageLoop createThreadId {window.CreateThreadId:X} current {Thread.CurrentThread.ManagedThreadId:X}");
                if (forceCancel)
                {
                    Logger.LogInformation("[vst window] force canceled");
                    break;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.LogInformation("[vst window] canceled");
                    Close();

                    var _ = 
                        Task.Delay(1000, CancellationToken.None)
                        .ContinueWith(
                            (task) => { forceCancel = true; },
                            TaskScheduler.Default
                        );
                }

                if (hWindow.Handle == IntPtr.Zero)
                {
                    Logger.LogInformation("[vst window] destroyed");
                    break;
                }

                {
                    //Logger.LogInformation($"[vst window] PeekMessage current {Thread.CurrentThread.ManagedThreadId:X}");
                    if (!NativeMethods.PeekMessage(ref msg, IntPtr.Zero, 0, 0, 0 /*NOREMOVE*/))
                    {
                        var res = NativeMethods.MsgWaitForMultipleObjects(0, IntPtr.Zero, false, 1000, 0x04FF /*QS_ALLINPUT*/);
                        if (res == 258/*WAIT_TIMEOUT*/)
                        {
                            //Logger.LogError($"[vst window] MsgWaitForMultipleObjects timeout.");
                            continue;
                        }
                        else if (res == 0/*WAIT_OBJECT_0 */)
                        {
                            //Logger.LogError($"[vst window] MsgWaitForMultipleObjects have message.");
                            continue;
                        }

                        Logger.LogError($"[vst window] MsgWaitForMultipleObjects failed {Marshal.GetLastWin32Error()}");
                        break;
                    }
                }
                //Logger.LogInformation($"[vst window] MSG {msg.hwnd:X} {msg.message:X} {msg.wParam:X} {msg.lParam:X} {msg.time} {msg.pt_x} {msg.pt_y}");

                var IsWindowUnicode = (msg.hwnd != IntPtr.Zero) && NativeMethods.IsWindowUnicode(new HandleRef(this, msg.hwnd));
                //Logger.LogInformation($"[vst window] IsWindowUnicode {IsWindowUnicode}");

                {
                    //Logger.LogInformation("[vst window] GetMessage");
                    var ret = IsWindowUnicode
                                ? NativeMethods.GetMessageW(ref msg, IntPtr.Zero, 0, 0)
                                : NativeMethods.GetMessageA(ref msg, IntPtr.Zero, 0, 0)
                                ;
                    //Logger.LogInformation($"[vst window] GetMessage {ret} {msg.hwnd:X} {msg.message:X} {msg.wParam:X} {msg.lParam:X} {msg.time} {msg.pt_x} {msg.pt_y}");

                    if (ret == -1)
                    {
                        Logger.LogError($"[vst window] GetMessage failed {Marshal.GetLastWin32Error()}");
                        break;
                    }
                    else if (ret == 0)
                    {
                        Logger.LogInformation($"[vst window] GetMessage Quit {msg.message:X}");
                        break;
                    }
                }

                {
                    //    Logger.LogInformation("[vst window] TranslateMessage");
                    var _ = NativeMethods.TranslateMessage(ref msg);
                    //    Logger.LogInformation($"[vst window] TranslateMessage {ret} {Marshal.GetLastWin32Error()}");
                }

                {
                    //    Logger.LogInformation("[vst window] DispatchMessage");
                    var _ = IsWindowUnicode
                                ? NativeMethods.DispatchMessageW(ref msg)
                                : NativeMethods.DispatchMessageA(ref msg)
                                ;
                    //    Logger.LogInformation($"[vst window] DispatchMessage {ret} {Marshal.GetLastWin32Error()}");
                }
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            //Logger.LogInformation($"[vst window] WndProc[{hwnd:X} {msg:X} {wParam:X} {lParam:X} current {Thread.CurrentThread.ManagedThreadId:X}");

            switch (msg)
            {
                case 0x0002://WM_DESTROY
                    hWindow = default;
                    NativeMethods.PostQuitMessage(0);
                    return IntPtr.Zero;

                case 0x0005://WM_SIZE
                    return IntPtr.Zero;

                case 0x0010://WM_CLOSE
                    try
                    {
                        onPreCloseWindow?.Invoke();
                    }
                    catch (Exception e)
                    {
                        Logger.LogError(e, "[vst window] onPreCloseWindow error");
                    }
                    //DefWindowProcに移譲
                    break;

                case 0x000F://WM_PAINT
                    NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
                    Capture(hwnd);
                    return IntPtr.Zero;

                case 0x0047://WM_WINDOWPOSCHANGED
                    return IntPtr.Zero;

            }
            return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private void Capture(IntPtr hwnd)
        {
            var hWindow = new HandleRef(this, hwnd);

            using var g = Graphics.FromImage(bitmap);
            var hdc = new HandleRef(this, g.GetHdc());
            try
            {
                NativeMethods.PrintWindow(hWindow, hdc, 0);
            }
            finally
            {
                g.ReleaseHdc(hdc.Handle);
            }
        }

        private void Capture1(IntPtr hwnd)
        {
            var hWindow = new HandleRef(this, hwnd);

            var rcClient = new NativeMethods.RECT();
            {
                var res = NativeMethods.GetClientRect(hWindow, ref rcClient);
                if (!res)
                {
                    Logger.LogError($"[vst window] GetClientRect failed {Marshal.GetLastWin32Error()}");
                    return;
                }
            }

            var cx = (int)(rcClient.right - rcClient.left);
            var cy = (int)(rcClient.bottom - rcClient.top);

            var hdcWindow = new HandleRef(this, NativeMethods.GetDC(hWindow));
            if (hdcWindow.Handle == default)
            {
                Logger.LogError($"[vst window] GetDC failed {Marshal.GetLastWin32Error()}");
                return;
            }
            try
            {
                var hdcMemDC = new HandleRef(this, NativeMethods.CreateCompatibleDC(hdcWindow));
                if (hdcMemDC.Handle == default)
                {
                    Logger.LogError($"[vst window] CreateCompatibleDC failed {Marshal.GetLastWin32Error()}");
                    return;
                }
                try
                {
                    var hbmScreen = new HandleRef(this,
                        NativeMethods.CreateCompatibleBitmap(
                            hdcWindow,
                            cx,
                            cy
                        ));
                    if (hbmScreen.Handle == default)
                    {
                        Logger.LogError($"[vst window] CreateCompatibleBitmap failed {Marshal.GetLastWin32Error()}");
                        return;
                    }
                    try
                    {
                        var hOld = NativeMethods.SelectObject(hdcMemDC, hbmScreen);
                        if (hOld == default)
                        {
                            Logger.LogError($"[vst window] SelectObject failed {Marshal.GetLastWin32Error()}");
                            return;
                        }

                        {
                            var res =
                                NativeMethods.BitBlt(
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
                                Logger.LogError($"[vst window] BitBlt failed {Marshal.GetLastWin32Error()}");
                            }
                        }

                        //TODO hbmScreenを転記する
                    }
                    finally
                    {
                        var res = NativeMethods.DeleteObject(hbmScreen);
                        if (!res)
                        {
                            Logger.LogError($"[vst window] DeleteObject failed {Marshal.GetLastWin32Error()}");
                        }
                    }
                }
                finally
                {
                    var res = NativeMethods.DeleteDC(hdcMemDC);
                    if (!res)
                    {
                        Logger.LogError($"[vst window] DeleteDC failed {Marshal.GetLastWin32Error()}");
                    }
                }
            }
            finally
            {
                var res = NativeMethods.ReleaseDC(hWindow, hdcWindow);
                if (res != 1)
                {
                    Logger.LogError($"[vst window] ReleaseDC failed {Marshal.GetLastWin32Error()}");
                }
            }
        }

        public void Close()
        {
            var IsWindowUnicode = NativeMethods.IsWindowUnicode(hWindow);
            var _ = IsWindowUnicode
                        ? NativeMethods.SendNotifyMessageW(hWindow, 0x0010, IntPtr.Zero, IntPtr.Zero)
                        : NativeMethods.SendNotifyMessageA(hWindow, 0x0010, IntPtr.Zero, IntPtr.Zero)
                        ;
        }
    }
}