using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Momiji.Interop.RTWorkQ;

internal static class Libraries
{
    public const string RTWorkQ = "RTWorkQ.dll";
}

internal static class NativeMethods
{
    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqRegisterPlatformEvents(
        [In] IRtwqPlatformEvents platformEvents
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqUnregisterPlatformEvents(
        [In] IRtwqPlatformEvents platformEvents
    );

    [ComImport]
    [Guid("63d9255a-7ff1-4b61-8faf-ed6460dacf2b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IRtwqPlatformEvents
    {
        [PreserveSig]
        int InitializationComplete();

        [PreserveSig]
        int ShutdownStart();

        [PreserveSig]
        int ShutdownComplete();
    }

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqLockPlatform();

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqUnlockPlatform();

    internal enum AVRT_PRIORITY
    {
        LOW = -1,
        NORMAL = 0,
        HIGH = 1,
        CRITICAL = 2
    }

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqRegisterPlatformWithMMCSS(
        [In][MarshalAs(UnmanagedType.LPWStr)] string usageClass,
        [In, Out] ref int taskId,
        [In] AVRT_PRIORITY lPriority
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqUnregisterPlatformFromMMCSS();

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqStartup();

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqShutdown();

    //一旦、SafeHandleでは無くす
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct WorkQueueId
    {
        private readonly uint id;
        internal uint Id => id;

        internal static WorkQueueId None => default;
    }

    //Lockカウントが増える
    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqLockSharedWorkQueue(
        [In][MarshalAs(UnmanagedType.LPWStr)] string usageClass,
        [In] AVRT_PRIORITY basePriority,
        [In, Out] ref int taskId,
        [Out] out WorkQueueId id
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqLockSharedWorkQueue(
        [In][MarshalAs(UnmanagedType.LPWStr)] string usageClass,
        [In] AVRT_PRIORITY basePriority,
        [In] nint taskId,
        [Out] out WorkQueueId id
    );

    //AddRef / Releaseの関係を表しているらしいので、WorkQueueに入れるときにLock / Responseが来たらUnlockが良さそう
    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqLockWorkQueue(
        [In] this WorkQueueId workQueueId
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqUnlockWorkQueue(
        [In] this WorkQueueId workQueueId
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqJoinWorkQueue(
        [In] this WorkQueueId workQueueId,
        [In] SafeHandle hFile,
        [Out] out nint out_
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqUnjoinWorkQueue(
        [In] this WorkQueueId workQueueId,
        [In] nint hFile
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqCreateAsyncResult(
        [In][MarshalAs(UnmanagedType.IUnknown)] object? appObject,
        [In] IRtwqAsyncCallback callback,
        [In][MarshalAs(UnmanagedType.IUnknown)] object? appState,
        [Out] out IRtwqAsyncResult asyncResult
    );

    //RtwqPutWorkItemとの違いは？
    //stateの中にIRtwqAsyncResultを持ったものをPutして、IRtwqAsyncCallback.Invokeで取り出し、呼び出し元に完了通知をするときに使うもの
    //らしいが、これもWorkQueueに入ってから実行されるのを待つ仕組みになってる様子
    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqInvokeCallback(
        [In] this IRtwqAsyncResult result
    );

    //IMFAsyncCallbackと同じGUID
    [ComImport]
    [Guid("a27003cf-2354-4f2a-8d6a-ab7cff15437e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IRtwqAsyncCallback
    {
        [PreserveSig]
        int GetParameters(
            [In][Out] ref uint pdwFlags,
            [In][Out] ref WorkQueueId pdwQueue
        );

        [PreserveSig]
        int Invoke(
            [In] IRtwqAsyncResult pAsyncResult
        );
    }

    //IMFAsyncResultと同じGUID
    [ComImport]
    [Guid("ac6b7889-0740-4d51-8619-905994a55cc6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IRtwqAsyncResult
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

    /*
     * 使わない
     * IRtwqAsyncResultの実装を行いたいときに用いる
        typedef struct tagRTWQASYNCRESULT : public IRtwqAsyncResult
        {
            OVERLAPPED overlapped;
            IRtwqAsyncCallback * pCallback;
            HRESULT hrStatusResult;
            DWORD dwBytesTransferred;
            HANDLE hEvent;
        }   RTWQASYNCRESULT;
     */

    internal enum RTWQ_WORKQUEUE_TYPE
    {
        RTWQ_STANDARD_WORKQUEUE = 0,      // single threaded MTA
        RTWQ_WINDOW_WORKQUEUE = 1,        // Message loop that calls PeekMessage() / DispatchMessage()..
        RTWQ_MULTITHREADED_WORKQUEUE = 2, // multithreaded MTA
    }

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqAllocateWorkQueue(
        [In] RTWQ_WORKQUEUE_TYPE WorkQueueType,
        [Out] out WorkQueueId workQueueId
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqAllocateSerialWorkQueue(
        [In] this WorkQueueId workQueueIdIn,
        [Out] out WorkQueueId workQueueIdOut
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqSetLongRunning(
        [In] this WorkQueueId dwQueue,
        [In][MarshalAs(UnmanagedType.Bool)] bool enable
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqGetWorkQueueMMCSSClass(
        [In] this WorkQueueId dwQueue,
        [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder usageClass,
        [In, Out] ref int usageClassLength
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqGetWorkQueueMMCSSTaskId(
        [In] this WorkQueueId dwQueue,
        [Out] out int taskId
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqGetWorkQueueMMCSSPriority(
        [In] this WorkQueueId dwQueue,
        [Out] out AVRT_PRIORITY priority
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqBeginRegisterWorkQueueWithMMCSS(
        [In] this WorkQueueId workQueueId,
        [In][MarshalAs(UnmanagedType.LPWStr)] string usageClass,
        [In] int dwTaskId,
        [In] AVRT_PRIORITY lPriority,
        [In] IRtwqAsyncCallback doneCallback,
        [In][MarshalAs(UnmanagedType.IUnknown)] object? doneState
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqEndRegisterWorkQueueWithMMCSS(
        [In] this IRtwqAsyncResult result,
        [Out] out nint taskId
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqBeginUnregisterWorkQueueWithMMCSS(
        [In] this WorkQueueId workQueueId,
        [In] IRtwqAsyncCallback doneCallback,
        [In][MarshalAs(UnmanagedType.IUnknown)] object? doneState
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqEndUnregisterWorkQueueWithMMCSS(
        [In] this IRtwqAsyncResult result
    );

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct RtWorkItemKey
    {
        private readonly ulong key;
        internal ulong Key => key;
        internal static RtWorkItemKey None => default;
    }

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqPutWorkItem(
        [In] this WorkQueueId dwQueue,
        [In] AVRT_PRIORITY lPriority,
        [In] IRtwqAsyncResult result
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqPutWaitingWorkItem(
        [In] SafeWaitHandle hEvent,
        [In] AVRT_PRIORITY lPriority,
        [In] IRtwqAsyncResult result,
        [Out] out RtWorkItemKey Key
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqScheduleWorkItem(
        [In] IRtwqAsyncResult result,
        [In] long Timeout,
        [Out] out RtWorkItemKey Key
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqCancelWorkItem(
        [In] this RtWorkItemKey Key
    );

    //STDAPI RtwqSetDeadline(DWORD workQueueId, LONGLONG deadlineInHNS, _Out_ HANDLE* pRequest);
    //STDAPI RtwqSetDeadline2(DWORD workQueueId, LONGLONG deadlineInHNS, LONGLONG preDeadlineInHNS, _Out_ HANDLE* pRequest);
    //STDAPI RtwqCancelDeadline(_In_ HANDLE pRequest);

    internal sealed class PeriodicCallbackKey : SafeHandleZeroOrMinusOneIsInvalid
    {
        public PeriodicCallbackKey() : base(true)
        {
        }

        protected override bool ReleaseHandle()
        {
            var hResult = NativeMethods.RtwqRemovePeriodicCallback(handle);
            return (hResult == 0);
        }

    }

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqAddPeriodicCallback(
        [In] nint /*RTWQPERIODICCALLBACK*/ Callback,
        [In][MarshalAs(UnmanagedType.IUnknown)] object /*IUnknown* */ context,
        [Out] out PeriodicCallbackKey key
    );

    [DllImport(Libraries.RTWorkQ, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int RtwqRemovePeriodicCallback(
        [In] nint dwKey
    );

    internal delegate void RtwqPeriodicCallback(
        [In][MarshalAs(UnmanagedType.IUnknown)] object /*IUnknown* */ context
    );
}
