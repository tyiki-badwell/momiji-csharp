using System.Runtime.InteropServices;
using System.Security.AccessControl;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

public class WindowException : Exception
{
    public WindowException(string message) : base(message)
    {
    }

    public WindowException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

internal class WindowSecurity : ObjectSecurity<User32.DESKTOP_ACCESS_MASK>
{
    public WindowSecurity()
        : base(
              false,
              ResourceType.WindowObject,
              (SafeHandle?)null,
              AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access //| AccessControlSections.Audit
          )
    {

    }
    public WindowSecurity(SafeHandle handle)
        : base(
              false,
              ResourceType.WindowObject,
              handle,
              AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access //| AccessControlSections.Audit
          )
    {

    }

    public new void Persist(SafeHandle handle)
    {
        Persist(
            handle,
            AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access //| AccessControlSections.Audit
        );
    }
}

public interface IWindowManager : IDisposable
{
    Task StartAsync(CancellationToken stoppingToken);
    void Cancel();

    public delegate void OnPreCloseWindow();
    public delegate void OnPostPaint(nint hWindow);

    public IWindow CreateWindow(
        OnPreCloseWindow? onPreCloseWindow = default,
        OnPostPaint? onPostPaint = default
    );

    void CloseAll();
}

public interface IWindow
{
    nint Handle
    {
        get;
    }
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

    bool SetWindowStyle(
        int style
    );
}
