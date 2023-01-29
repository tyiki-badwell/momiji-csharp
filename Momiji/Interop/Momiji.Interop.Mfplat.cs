using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Momiji.Interop.Mfplat;

internal static class Libraries
{
    public const string Mfplat = "Mfplat.dll";
}

internal static class NativeMethods
{

    internal enum MFSTARTUP : uint
    {
        NOSOCKET = 0x1,
        LITE = 0x1,
        FULL = 0,
    }

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFStartup(
        [In] uint Version,
        [In] MFSTARTUP dwFlags = MFSTARTUP.FULL
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFShutdown();

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFLockPlatform();

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFUnlockPlatform();

    //MFPutWorkItem
    //MFPutWorkItem2
    //MFPutWorkItemEx

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFPutWorkItemEx2(
        [In] this WorkQueueId dwQueue,
        [In] int Priority,
        [In] IMFAsyncResult pResult
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFPutWaitingWorkItem(
        [In] SafeWaitHandle hEvent,
        [In] int Priority,
        [In] IMFAsyncResult pResult,
        [Out] out MFWorkItemKey pKey
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFAllocateSerialWorkQueue(
        [In] this WorkQueueId workQueueIdIn,
        [Out] out WorkQueueId workQueueIdOut
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFScheduleWorkItemEx(
        [In] IMFAsyncResult pResult,
        [In] long Timeout,
        [Out] out MFWorkItemKey pKey
    );

    //MFScheduleWorkItem

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFCancelWorkItem(
        [In] this MFWorkItemKey Key
    );

    //MFGetTimerPeriodicity

    internal delegate void MfPeriodicCallback(
        [In][MarshalAs(UnmanagedType.IUnknown)] object /*IUnknown* */ pContext
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFAddPeriodicCallback(
        [In] nint /*MFPERIODICCALLBACK*/ Callback,
        [In][MarshalAs(UnmanagedType.IUnknown)] object /*IUnknown* */ pContext,
        [Out] out PeriodicCallbackKey pdwKey
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFRemovePeriodicCallback(
        [In] nint dwKey
    );


    internal enum MFASYNC_WORKQUEUE_TYPE
    {
        MF_STANDARD_WORKQUEUE = 0,      // Work queue in a thread without Window message loop.
        MF_WINDOW_WORKQUEUE = 1,        // Work queue in a thread running Window Message loop that calls PeekMessage() / DispatchMessage()..
        MF_MULTITHREADED_WORKQUEUE = 2, // common MT threadpool
    }

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFAllocateWorkQueueEx(
        [In] MFASYNC_WORKQUEUE_TYPE WorkQueueType,
        [Out] out WorkQueueId pdwWorkQueue
    );

    //MFAllocateWorkQueue

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFLockWorkQueue(
        [In] this WorkQueueId dwWorkQueue
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFUnlockWorkQueue(
        [In] this WorkQueueId dwWorkQueue
    );

    /*
STDAPI MFBeginRegisterWorkQueueWithMMCSS(
            DWORD dwWorkQueueId,
            _In_ LPCWSTR wszClass,
            DWORD dwTaskId,
            _In_ IMFAsyncCallback * pDoneCallback,
            _In_ IUnknown * pDoneState );

STDAPI MFBeginRegisterWorkQueueWithMMCSSEx(
            DWORD dwWorkQueueId,
            _In_ LPCWSTR wszClass,
            DWORD dwTaskId,
            LONG lPriority,
            _In_ IMFAsyncCallback * pDoneCallback,
            _In_ IUnknown * pDoneState );

STDAPI MFEndRegisterWorkQueueWithMMCSS(
            _In_ IMFAsyncResult * pResult,
            _Out_ DWORD * pdwTaskId );

STDAPI MFBeginUnregisterWorkQueueWithMMCSS(
            DWORD dwWorkQueueId,
            _In_ IMFAsyncCallback * pDoneCallback,
            _In_ IUnknown * pDoneState );

STDAPI MFEndUnregisterWorkQueueWithMMCSS(
            _In_ IMFAsyncResult * pResult );
     */

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFGetWorkQueueMMCSSClass(
        [In] this WorkQueueId dwWorkQueueId,
        [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszClass,
        [In, Out] ref int pcchClass
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFGetWorkQueueMMCSSTaskId(
        [In] this WorkQueueId dwWorkQueueId,
        [Out] out int pdwTaskId
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFRegisterPlatformWithMMCSS(
        [In][MarshalAs(UnmanagedType.LPWStr)] string wszClass,
        [In, Out] ref int pdwTaskId,
        [In] int lPriority
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFUnregisterPlatformFromMMCSS();


    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFLockSharedWorkQueue(
        [In][MarshalAs(UnmanagedType.LPWStr)] string wszClass,
        [In] int BasePriority,
        [In, Out] ref int pdwTaskId,
        [Out] out WorkQueueId pID
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFLockSharedWorkQueue(
        [In][MarshalAs(UnmanagedType.LPWStr)] string wszClass,
        [In] int BasePriority,
        [In] nint pdwTaskId,
        [Out] out WorkQueueId pID
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFGetWorkQueueMMCSSPriority(
        [In] this WorkQueueId dwWorkQueueId,
        [Out] out int lPriority
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFCreateAsyncResult(
        [In][MarshalAs(UnmanagedType.IUnknown)] object? punkObject,
        [In] IMFAsyncCallback pCallback,
        [In][MarshalAs(UnmanagedType.IUnknown)] object? punkState,
        [Out] out IMFAsyncResult ppAsyncResult
    );

    [DllImport(Libraries.Mfplat, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int MFInvokeCallback(
        [In] this IMFAsyncResult pAsyncResult
    );

    /*
     * IRtwqAsyncResultの実装を行いたいときに用いる
            typedef struct tagMFASYNCRESULT : public IMFAsyncResult
            {
                OVERLAPPED overlapped;
                IMFAsyncCallback * pCallback;
                HRESULT hrStatusResult;
                DWORD dwBytesTransferred;
                HANDLE hEvent;
            }   MFASYNCRESULT;
     */


    [Guid("ac6b7889-0740-4d51-8619-905994a55cc6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFAsyncResult
    {
        [PreserveSig]
        int GetState(
            [Out][MarshalAs(UnmanagedType.IUnknown)] out object? /*IUnknown*/ ppunkState
        );

        [PreserveSig]
        int GetStatus();

        [PreserveSig]
        int SetStatus(
            int hrStatus
        );

        [PreserveSig]
        int GetObject(
            [Out][MarshalAs(UnmanagedType.IUnknown)] out object? /*IUnknown*/ ppObject
        );

        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Bool)]
        object? /*IUnknown*/ GetStateNoAddRef();
    }

    [Guid("a27003cf-2354-4f2a-8d6a-ab7cff15437e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFAsyncCallback
    {
        [PreserveSig]
        int GetParameters(
            ref uint pdwFlags, 
            ref WorkQueueId pdwQueue
        );

        [PreserveSig]
        int Invoke(
            IMFAsyncResult pAsyncResult
        );
    }

    [Guid("c7a4dca1-f5f0-47b6-b92b-bf0106d25791"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFAsyncCallbackLogging
    {
        [PreserveSig]
        int GetParameters(
            ref uint pdwFlags,
            ref WorkQueueId pdwQueue
        );

        [PreserveSig]
        int Invoke(
            IMFAsyncResult pAsyncResult
        );

        [PreserveSig]
        nint GetObjectPointer();

        [PreserveSig]
        uint GetObjectTag();
    }
}

//一旦、SafeHandleでは無くす
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct WorkQueueId
{
    private readonly uint id;
    internal uint Id => id;

    internal static WorkQueueId None => default;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct MFWorkItemKey
{
    private readonly ulong key;
    internal ulong Key => key;
    internal static MFWorkItemKey None => default;
}

internal sealed class PeriodicCallbackKey : SafeHandleZeroOrMinusOneIsInvalid
{
    public PeriodicCallbackKey() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        var hResult = NativeMethods.MFRemovePeriodicCallback(handle);
        return (hResult == 0);
    }

}
