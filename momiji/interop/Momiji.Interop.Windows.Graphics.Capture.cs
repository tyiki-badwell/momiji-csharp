using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace Momiji.Interop.Windows.Graphics.Capture
{
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(
            [In] IntPtr window,
            [In] ref Guid iid);

        IntPtr CreateForMonitor(
            [In] IntPtr monitor,
            [In] ref Guid iid);
    }


    public static class GraphicsCaptureItemInterop
    {
        private static readonly IGraphicsCaptureItemInterop interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();

        //static readonly Guid GraphicsCaptureItemGuid = GuidGenerator.CreateIID(typeof(GraphicsCaptureItem).GetInterface("Windows.Graphics.Capture.IGraphicsCaptureItem"));
        public static GraphicsCaptureItem CreateForWindow(this IntPtr hWnd)
        {
            var GraphicsCaptureItemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            var ptr = interop.CreateForWindow(hWnd, ref GraphicsCaptureItemGuid);
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
}