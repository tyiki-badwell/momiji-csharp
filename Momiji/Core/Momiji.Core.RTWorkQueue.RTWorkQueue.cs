using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using Momiji.Core.Threading;
using Momiji.Interop.RTWorkQ;
using RTWorkQ = Momiji.Interop.RTWorkQ.NativeMethods;

namespace Momiji.Core.RTWorkQueue;

[SupportedOSPlatform("windows")]
public class RTWorkQueue : IRTWorkQueue
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RTWorkQueue> _logger;
    private bool _disposed;
    private bool _shutdown;
    internal ApartmentType CreatedApartmentType { get; init; }

    private readonly RTWorkQueueManager _parent;
    private readonly RTWorkQ.WorkQueueId _workQueueid;

    private readonly int _taskId;

    private RTWorkQueue(
        ILoggerFactory loggerFactory,
        RTWorkQueueManager parent
    )
    {
        //STAからの呼び出しはサポート外にする
        ApartmentType.CheckNeedMTA();

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<RTWorkQueue>();
        _parent = parent;
        CreatedApartmentType = ApartmentType.GetApartmentType();
    }

    internal RTWorkQueue(
        ILoggerFactory loggerFactory,
        RTWorkQueueManager parent,
        string usageClass,
        IRTWorkQueue.TaskPriority basePriority,
        int taskId
    ) : this(loggerFactory, parent)
    {
        _logger.LogTrace($"create RTWorkQueue(shared) {CreatedApartmentType}");

        if (usageClass != "")
        {
            var putTaskId = taskId;

            //Lock +1
            _logger.LogTrace($"RtwqLockSharedWorkQueue usageClass:{usageClass} basePriority:{basePriority} taskId:{putTaskId:X}");
            Marshal.ThrowExceptionForHR(RTWorkQ.RtwqLockSharedWorkQueue(
                usageClass,
                (RTWorkQ.AVRT_PRIORITY)basePriority,
                ref putTaskId,
                out _workQueueid
            ));

            _taskId = putTaskId;
            _logger.LogDebug($"RTWorkQueue(shared) Class:{usageClass} Priority:{basePriority} TaskId:{_taskId:X} QueueId:{_workQueueid.Id:X}");
        }
        else
        {
            //usageClass="" のときは、taskIdにnullを渡す必要アリ
            _logger.LogTrace("RtwqLockSharedWorkQueue usageClass:'' basePriority:0 taskId:null");
            Marshal.ThrowExceptionForHR(RTWorkQ.RtwqLockSharedWorkQueue(
                usageClass,
                0,
                nint.Zero,
                out _workQueueid
            ));
            _logger.LogDebug($"RTWorkQueue(shared) Class:'' Priority:0 TaskId:null QueueId:{_workQueueid.Id:X}");
        }
    }

    internal RTWorkQueue(
        ILoggerFactory loggerFactory,
        RTWorkQueueManager parent,
        IRTWorkQueue.WorkQueueType type
    ) : this(loggerFactory, parent)
    {
        _logger.LogTrace($"create RTWorkQueue(private) {CreatedApartmentType}");

        //Lock +1
        _logger.LogTrace($"RtwqAllocateWorkQueue type:{type}");
        Marshal.ThrowExceptionForHR(RTWorkQ.RtwqAllocateWorkQueue(
            (RTWorkQ.RTWQ_WORKQUEUE_TYPE)type,
            out _workQueueid
        ));

        _logger.LogDebug($"RTWorkQueue(private) type:{type} QueueId:{_workQueueid.Id:X}");
    }

    internal RTWorkQueue(
        ILoggerFactory loggerFactory,
        RTWorkQueueManager parent,
        RTWorkQueue workQueue
    ) : this(loggerFactory, parent)
    {
        _logger.LogTrace($"create RTWorkQueue(serial) {CreatedApartmentType}");

        //Lock +1
        _logger.LogTrace($"RtwqAllocateSerialWorkQueue parent.QueueId:{workQueue._workQueueid.Id:X}");
        Marshal.ThrowExceptionForHR(RTWorkQ.RtwqAllocateSerialWorkQueue(
            workQueue._workQueueid,
            out _workQueueid
        ));

        _logger.LogDebug($"RTWorkQueue(serial) QueueId:{_workQueueid.Id:X}");
    }

    ~RTWorkQueue()
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
        _shutdown = true;

        if (_disposed)
        {
            _logger.LogDebug($"Disposed {CreatedApartmentType}");
            return;
        }

        _logger.LogTrace($"Dispose start {CreatedApartmentType}");

        if (disposing)
        {
        }

        //TODO STAから呼ばれたときはMTAに移動して解放しないとダメ？

        if (_workQueueid.Id != default)
        {
            try
            {
                Marshal.ThrowExceptionForHR(_workQueueid.RtwqUnlockWorkQueue());
            }
            catch (Exception e)
            {
                _logger.LogError(e, "RtwqUnlockWorkQueue failed");
            }
        }

        _disposed = true;
        _logger.LogTrace($"Dispose end {CreatedApartmentType}");
    }

    private void CheckShutdown()
    {
        if (_shutdown)
        {
            throw new InvalidOperationException("in shutdown.");
        }
    }

    public int GetMMCSSTaskId()
    {
        //STAからの呼び出しはサポート外にする
        ApartmentType.CheckNeedMTA();

        Marshal.ThrowExceptionForHR(_workQueueid.RtwqGetWorkQueueMMCSSTaskId(out var taskId));
        return taskId;
    }
    public IRTWorkQueue.TaskPriority GetMMCSSPriority()
    {
        //STAからの呼び出しはサポート外にする
        ApartmentType.CheckNeedMTA();

        Marshal.ThrowExceptionForHR(_workQueueid.RtwqGetWorkQueueMMCSSPriority(out var priority));
        return (IRTWorkQueue.TaskPriority)priority;
    }

    public string GetMMCSSClass()
    {
        //STAからの呼び出しはサポート外にする
        ApartmentType.CheckNeedMTA();

        var text = new StringBuilder();
        var length = 0;

        {
            var result = _workQueueid.RtwqGetWorkQueueMMCSSClass(text, ref length);
            if (result != unchecked((int)0xC00D36B1)) //E_BUFFERTOOSMALL
            {
                var e = Marshal.GetExceptionForHR(result);
                if (e != null)
                {
                    throw e;
                }
                _logger.LogTrace("GetMMCSSClass ''");
                return "";
            }
        }

        if (length == 1)
        { //null terminated のみだった
            _logger.LogTrace("GetMMCSSClass ''");
            return "";
        }

        if (text.Capacity < length)
        { //既定の容量以下にはできない
            text.Capacity = length;
        }

        {
            var result = _workQueueid.RtwqGetWorkQueueMMCSSClass(text, ref length);
            {
                var e = Marshal.GetExceptionForHR(result);
                if (e != null)
                {
                    throw e;
                }
                var text_ = text.ToString();
                _logger.LogTrace($"GetMMCSSClass {text_}");
                return text_;
            }
        }
    }

    [ClassInterface(ClassInterfaceType.None)]
    private class RegisterMMCSSAsyncCallback : RTWorkQ.IRtwqAsyncCallback
    {
        public int GetParameters(ref uint pdwFlags, ref RTWorkQ.WorkQueueId pdwQueue) {
            return unchecked((int)0x80004001); //E_NOTIMPL
        }

        public int Invoke(RTWorkQ.IRtwqAsyncResult pAsyncResult)
        {
            pAsyncResult.GetState(out var obj);
            if (obj is not Func<RTWorkQ.IRtwqAsyncResult, int> func)
            {
                return 0;
            }
            return func.Invoke(pAsyncResult);
        }
    }

    public Task RegisterMMCSSAsync(
        string usageClass,
        IRTWorkQueue.TaskPriority basePriority,
        int taskId
    )
    {
        //STAからの呼び出しはサポート外にする
        ApartmentType.CheckNeedMTA();

        CheckShutdown();

        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);
        var callback = new RegisterMMCSSAsyncCallback();

        _logger.LogTrace($"RtwqBeginRegisterWorkQueueWithMMCSS usageClass:{usageClass} taskId:{taskId:X} basePriority:{basePriority}");

        Marshal.ThrowExceptionForHR(_workQueueid.RtwqBeginRegisterWorkQueueWithMMCSS(
            usageClass,
            taskId,
            (RTWorkQ.AVRT_PRIORITY)basePriority,
            callback,
            (RTWorkQ.IRtwqAsyncResult result) => {
                try
                {
                    _logger.LogTrace("RtwqEndRegisterWorkQueueWithMMCSS");
                    Marshal.ThrowExceptionForHR(result.RtwqEndRegisterWorkQueueWithMMCSS(out var taskId));
                    _logger.LogTrace($"RtwqEndRegisterWorkQueueWithMMCSS result taskId:{taskId:X}");
                    tcs.SetResult();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "error RtwqEndRegisterWorkQueueWithMMCSS");
                    tcs.SetException(e);
                }
                return 0;
            }
        ));

        return tcs.Task;
    }

    public Task UnregisterMMCSSAsync()
    {
        //STAからの呼び出しはサポート外にする
        ApartmentType.CheckNeedMTA();

        CheckShutdown();

        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);
        var callback = new RegisterMMCSSAsyncCallback();

        _logger.LogTrace("RtwqBeginUnregisterWorkQueueWithMMCSS");

        Marshal.ThrowExceptionForHR(_workQueueid.RtwqBeginUnregisterWorkQueueWithMMCSS(
            callback,
            (RTWorkQ.IRtwqAsyncResult result) => {
                try
                {
                    _logger.LogTrace("RtwqEndUnregisterWorkQueueWithMMCSS");
                    Marshal.ThrowExceptionForHR(result.RtwqEndUnregisterWorkQueueWithMMCSS());
                    tcs.SetResult();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "error RtwqEndUnregisterWorkQueueWithMMCSS");
                    tcs.SetException(e);
                }
                return 0;
            }
        ));

        return tcs.Task;
    }


    public void SetLongRunning(bool enable)
    {
        //STAからの呼び出しはサポート外にする
        ApartmentType.CheckNeedMTA();

        CheckShutdown();

        Marshal.ThrowExceptionForHR(_workQueueid.RtwqSetLongRunning(enable));
    }

    public void PutWorkItem(
        IRTWorkQueue.TaskPriority priority,
        Action action,
        Action<Exception?, CancellationToken>? afterAction = default
    )
    {
        //QueueとAsyncResultを作ったApartmentTypeが一致している必要がある
        //STAからの呼び出しはサポート外にする
        ApartmentType.CheckNeedMTA();

        CheckShutdown();

        var asyncResult = _parent.GetAsyncResult(0, _workQueueid, action, afterAction);

        try
        {
            _logger.LogTrace($"PutWorkItem Id:{asyncResult.Id} {asyncResult.CreatedApartmentType}");
            asyncResult.WaitingToRun();
            Marshal.ThrowExceptionForHR(_workQueueid.RtwqPutWorkItem(
                (RTWorkQ.AVRT_PRIORITY)priority,
                asyncResult.RtwqAsyncResult
            ));
        }
        catch
        {
            _parent.ReleaseAsyncResult(asyncResult);
            throw;
        }
    }

    public Task PutWorkItemAsync(
        IRTWorkQueue.TaskPriority priority,
        Action action
    )
    {
        return _parent.ToAsync(
            (afterAction_) => {
                PutWorkItem(priority, action, afterAction_);
            }
        );
    }
}
