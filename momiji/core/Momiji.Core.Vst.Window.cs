using Microsoft.Extensions.Logging;
using Momiji.Interop;
using Momiji.Interop.Kernel32;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;

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

    public class EditorWindow2 : HwndHost
    {
        private IntPtr hwndHost;

        private ILogger Logger { get; }
        public EditorWindow2(ILoggerFactory loggerFactory)
        {
            Logger = loggerFactory.CreateLogger<EditorWindow2>();
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            var X = 10;
            var Y = 10;
            var Width = 500;
            var Height = 500;
            var Style =
                0x00000000 //WS_OVERLAPPED
                | 0x00C00000 //WS_CAPTION
                | 0x00080000 //WS_SYSMENU
                | 0x00040000 //WS_THICKFRAME
                | 0x10000000 //WS_VISIBLE
                ;

            hwndHost =
                SafeNativeMethods.CreateWindowEx(
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    Style,
                    X,
                    Y,
                    Width,
                    Height,
                    hwndParent.Handle,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);

            return new HandleRef(this, hwndHost);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            SafeNativeMethods.DestroyWindow(hwnd.Handle);
        }
        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            handled = false;
            return IntPtr.Zero;
        }
    }

    /*
    public class EditorWindow : NativeWindow, IDisposable
    {
        private bool disposed;
        private ILogger Logger { get; }
        public EditorWindow(ILoggerFactory loggerFactory)
        {
            Logger = loggerFactory.CreateLogger<EditorWindow>();
            CreateParams cp = new();
            cp.X = 10;
            cp.Y = 10;
            cp.Width = 500;
            cp.Height = 500;
            cp.Parent = default;
            cp.Style =
                0x00000000 //WS_OVERLAPPED
                | 0x00C00000 //WS_CAPTION
                | 0x00080000 //WS_SYSMENU
                | 0x00040000 //WS_THICKFRAME
                | 0x10000000 //WS_VISIBLE
                ;

            CreateHandle(cp);
        }
        protected override void WndProc(ref Message m)
        {
            Logger.LogInformation($"editor {m.Msg:X} {m.Result:X} {m.WParam:X} {m.LParam:X}");

            switch (m.Msg)
            {
                case 0x0001://WM_CREATE
                    {
                        m.Result = (IntPtr)0;
                        return;
                    }

            }
            base.WndProc(ref m);
        }

        ~EditorWindow()
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
                Logger.LogInformation($"[vst editor] dispose");
                DestroyHandle();
            }

            disposed = true;
        }
    }
    */

    public class VstWindow : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        private bool disposed;

        private PinnedDelegate<SafeNativeMethods.WNDPROC> wndProc;

        private SafeNativeMethods.WNDCLASS windowClass;

        private IntPtr className;

        public IntPtr Handle { get; private set; }

        public VstWindow(
            ILoggerFactory loggerFactory
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<VstWindow>();

            wndProc = new((new(WndProc)));

            className = Marshal.StringToHGlobalUni(nameof(VstWindow));

            windowClass = new SafeNativeMethods.WNDCLASS
            {
                style = SafeNativeMethods.WNDCLASS.CS.HREDRAW | SafeNativeMethods.WNDCLASS.CS.VREDRAW,
                lpfnWndProc = wndProc.FunctionPointer,
                hInstance = SafeNativeMethods.GetModuleHandle(null),
                lpszClassName = className
            };

            var atom = SafeNativeMethods.RegisterClass(ref windowClass);
            Logger.LogInformation($"[vst window] RegisterClass {atom}");

            var editorTask = Task.Run(() =>
            {
                var thread = new Thread(new ThreadStart(WindowThread));
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
            });



        }

        ~VstWindow()
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
                wndProc?.Dispose();
                wndProc = default;

                Marshal.FreeHGlobal(className);
            }

            disposed = true;
        }

        private void WindowThread()
        {
            var X = 10;
            var Y = 10;
            var Width = 500;
            var Height = 500;
            var Style =
                0x00000000 //WS_OVERLAPPED
                | 0x00C00000 //WS_CAPTION
                | 0x00080000 //WS_SYSMENU
                | 0x00040000 //WS_THICKFRAME
                | 0x10000000 //WS_VISIBLE
                ;

            Handle =
               SafeNativeMethods.CreateWindowEx(
                   0,
                   className,
                   IntPtr.Zero,
                   Style,
                   X,
                   Y,
                   Width,
                   Height,
                   IntPtr.Zero,
                   IntPtr.Zero,
                   windowClass.hInstance,
                   IntPtr.Zero
               );
            Logger.LogInformation($"[vst window] CreateWindowEx {Handle} {Marshal.GetLastWin32Error()}");

            using var msg = new PinnedBuffer<SafeNativeMethods.MSG>(new SafeNativeMethods.MSG());
            while (true)
            {
                Logger.LogInformation("[vst window] try GetMessage");
                var ret = SafeNativeMethods.GetMessage(msg.AddrOfPinnedObject, Handle, 0, 0);
                Logger.LogInformation($"[vst window] GetMessage {msg.Target.message:X}");
                if (ret == -1)
                {
                    Logger.LogInformation("[vst window] GetMessage failed");
                    break;
                }
                else if (ret == 0)
                {
                    Logger.LogInformation($"[vst window] GetMessage Quit {msg.Target.message:X}");
                    break;
                }

                Logger.LogInformation($"[vst window] TranslateMessage {msg.Target.message:X}");
                SafeNativeMethods.TranslateMessage(msg.AddrOfPinnedObject);
                Logger.LogInformation($"[vst window] DispatchMessage {msg.Target.message:X}");
                SafeNativeMethods.DispatchMessage(msg.AddrOfPinnedObject);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            Logger.LogInformation($"[vst window] WndProc {msg:X} {wParam:X} {lParam:X}");

            switch(msg)
            {
                case 0x0081://WM_NCCREATE
                    {
                        return new IntPtr(1);
                    }
            }

            return IntPtr.Zero;
        }

    }
}