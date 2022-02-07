using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace Momiji.Interop.Windows.Graphics.Capture;

[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    IntPtr CreateForWindow(
        [In] HandleRef window,
        [In] ref Guid iid);

    IntPtr CreateForMonitor(
        [In] HandleRef monitor,
        [In] ref Guid iid);
}


public static class GraphicsCaptureItemInterop
{
    private static readonly IGraphicsCaptureItemInterop interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();

    internal static GraphicsCaptureItem CreateForWindow(HandleRef hWindow)
    {
        var GraphicsCaptureItemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        var ptr = interop.CreateForWindow(hWindow, ref GraphicsCaptureItemGuid);
        try
        {
            return GraphicsCaptureItem.FromAbi(ptr);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

}
