using System.Runtime.InteropServices;

namespace Momiji.Interop.Gdi32;

internal static class Libraries
{
    public const string Gdi32 = "gdi32.dll";
}

internal static class NativeMethods
{
    [DllImport(Libraries.Gdi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr CreateCompatibleDC(
        [In] HandleRef hdc
    );

    [DllImport(Libraries.Gdi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(
        [In] HandleRef hdc
    );

    [DllImport(Libraries.Gdi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr CreateCompatibleBitmap(
        [In] HandleRef hdc,
        [In] int cx,
        [In] int cy
    );

    [DllImport(Libraries.Gdi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(
        [In] HandleRef ho
    );

    [DllImport(Libraries.Gdi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr SelectObject(
        [In] HandleRef hdc,
        [In] HandleRef h
    );


    [DllImport(Libraries.Gdi32, CallingConvention = CallingConvention.Winapi, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BitBlt(
        [In] HandleRef hdc,
        [In] int x,
        [In] int y,
        [In] int cx,
        [In] int cy,
        [In] HandleRef hdcSrc,
        [In] int x1,
        [In] int y1,
        [In] uint rop
    );
}
