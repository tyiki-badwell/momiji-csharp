using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Core.Cache;
using Momiji.Core.Threading;
using RTWorkQ = Momiji.Interop.RTWorkQ.NativeMethods;

namespace Momiji.Core.RTWorkQueue;

[SupportedOSPlatform("windows")]
public class RTWorkQueuePlatformEventsHandler : IRTWorkQueuePlatformEventsHandler
{
    private readonly ILogger<RTWorkQueuePlatformEventsHandler> _logger;
    private bool _disposed;

    private readonly RtwqPlatformEvents? _rtwqPlatformEvents;

    //TODO これに連動して行うべき動作があるか？
    [ClassInterface(ClassInterfaceType.None)]
    private class RtwqPlatformEvents : RTWorkQ.IRtwqPlatformEvents
    {
        private readonly ILogger<RtwqPlatformEvents> _logger;
        public RtwqPlatformEvents(
            ILoggerFactory loggerFactory
        )
        {
            _logger = loggerFactory.CreateLogger<RtwqPlatformEvents>();
        }

        public int InitializationComplete()
        {
            _logger.LogDebug("RtwqPlatformEvents.InitializationComplete");
            return 0;
        }

        public int ShutdownComplete()
        {
            _logger.LogDebug("RtwqPlatformEvents.ShutdownComplete");
            return 0;
        }
        public int ShutdownStart()
        {
            _logger.LogDebug("RtwqPlatformEvents.ShutdownStart");
            return 0;
        }
    }

    public RTWorkQueuePlatformEventsHandler(
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger<RTWorkQueuePlatformEventsHandler>();
        _rtwqPlatformEvents = new(loggerFactory);

        try
        {
            _logger.LogTrace("RtwqRegisterPlatformEvents");
            Marshal.ThrowExceptionForHR(RTWorkQ.RtwqRegisterPlatformEvents(_rtwqPlatformEvents));
        }
        catch (COMException e)
        {
            _logger.LogError(e, "failed RtwqRegisterPlatformEvents");
            _rtwqPlatformEvents = null;
        }
    }

    ~RTWorkQueuePlatformEventsHandler()
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
        if (_disposed)
        {
            return;
        }

        if (_rtwqPlatformEvents != null)
        {
            try
            {
                _logger.LogTrace("RtwqUnregisterPlatformEvents");
                Marshal.ThrowExceptionForHR(RTWorkQ.RtwqUnregisterPlatformEvents(_rtwqPlatformEvents));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "RtwqUnregisterPlatformEvents failed");
            }
        }

        _disposed = true;
        _logger.LogDebug("Dispose");
    }
}


[SupportedOSPlatform("windows")]
public class RTWorkQueueManager : IRTWorkQueueManager
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RTWorkQueueManager> _logger;
    private bool _disposed;

    private readonly Pool<uint, RTWorkQueueAsyncResultPoolValue> _pool;

    private uint _asyncResultId = 0;

    private bool _shutdown;

    internal ApartmentType CreatedApartmentType { get; init; }

    private record class Param
    {
        public string UsageClass { get; set; } = "";
        public IRTWorkQueue.TaskPriority BasePriority { get; set; } = IRTWorkQueue.TaskPriority.NORMAL;
        public int TaskId { get; set; } = 0;
    }

    private readonly Param _param = new();

    public RTWorkQueueManager(
        IConfiguration configuration,
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        {
            var key = typeof(RTWorkQueueManager).FullName ?? throw new Exception("typeof(WorkQueueManager).FullName is null");
            var section = configuration.GetSection(key);
            section.Bind(_param);
        }

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<RTWorkQueueManager>();

        CreatedApartmentType = ApartmentType.GetApartmentType();

        _logger.LogTrace($"create {CreatedApartmentType}");

        //TODO これの呼び出しはプロセス単位？スレッド単位？ STAかどうかは気にする？
        _logger.LogTrace("RtwqStartup");
        Marshal.ThrowExceptionForHR(RTWorkQ.RtwqStartup());

        //TODO これの呼び出しはプロセス単位？スレッド単位？ STAかどうかは気にする？
        _logger.LogTrace("RtwqLockPlatform");
        Marshal.ThrowExceptionForHR(RTWorkQ.RtwqLockPlatform());

        if (_param.UsageClass != "")
        {
            //TODO タスクが動こうとしたときにセットするよう延期する
            RegisterMMCSS(
                _param.UsageClass,
                _param.BasePriority,
                _param.TaskId
            );
        }

        _pool = new(
            () => {
                RTWorkQueueAsyncResultPoolValue asyncResult = new(_loggerFactory, this);
                return (asyncResult.Id, asyncResult);
            }, 
            _loggerFactory
        );
    }

    ~RTWorkQueueManager()
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

        var apartmentType = ApartmentType.GetApartmentType();

        if (_disposed)
        {
            _logger.LogDebug($"Disposed {CreatedApartmentType} / current:{apartmentType}");
            return;
        }

        _logger.LogDebug($"Dispose start {CreatedApartmentType} / current:{apartmentType}");

        if (disposing)
        {
            _pool.Dispose();
        }

        if (_param.TaskId != 0)
        {
            try
            {
                UnregisterMMCSS();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "RtwqUnregisterPlatformFromMMCSS failed");
            }
        }

        try
        {
            _logger.LogTrace("RtwqUnlockPlatform");
            Marshal.ThrowExceptionForHR(RTWorkQ.RtwqUnlockPlatform());
        }
        catch (Exception e)
        {
            _logger.LogError(e, "RtwqUnlockPlatform failed");
        }

        try
        {
            _logger.LogTrace("RtwqShutdown");
            Marshal.ThrowExceptionForHR(RTWorkQ.RtwqShutdown());
        }
        catch (Exception e)
        {
            _logger.LogError(e, "RtwqShutdown failed");
        }

        _disposed = true;
        _logger.LogDebug($"Dispose end {CreatedApartmentType} / current:{apartmentType}");
    }

    private void CheckShutdown()
    {
        if (_shutdown)
        {
            throw new InvalidOperationException("in shutdown.");
        }
    }

    internal uint GenerateIdAsyncResult()
    {
        return Interlocked.Increment(ref _asyncResultId);
    }

    internal RTWorkQueueAsyncResultPoolValue GetAsyncResult(
        uint flags,
        RTWorkQ.WorkQueueId queue,
        Action action,
        Action<Exception?, CancellationToken>? afterAction = default
    )
    {
        CheckShutdown();

        var asyncResult = _pool.Get();

        asyncResult.Initialize(flags, queue, action, afterAction);
        return asyncResult;
    }

    internal void ReleaseAsyncResult(RTWorkQueueAsyncResultPoolValue asyncResult)
    {
        _pool.Release(asyncResult.Id);
    }

    public void RegisterMMCSS(
        string usageClass,
        IRTWorkQueue.TaskPriority basePriority = IRTWorkQueue.TaskPriority.NORMAL,
        int taskId = 0
    )
    {
        CheckShutdown();

        _logger.LogTrace($"RtwqRegisterPlatformWithMMCSS usageClass:{usageClass} taskId:{taskId} basePriority:{basePriority}");
        Marshal.ThrowExceptionForHR(RTWorkQ.RtwqRegisterPlatformWithMMCSS(
            usageClass,
            ref taskId,
            (RTWorkQ.AVRT_PRIORITY)basePriority
        ));

        _param.UsageClass = usageClass;
        _param.TaskId = taskId;
        _param.BasePriority = basePriority;

        _logger.LogInformation($"{_param.UsageClass} taskId:{_param.TaskId:X}");
    }

    public void UnregisterMMCSS()
    {
        _logger.LogTrace("RtwqUnregisterPlatformFromMMCSS");
        Marshal.ThrowExceptionForHR(RTWorkQ.RtwqUnregisterPlatformFromMMCSS());

        _param.UsageClass = "";
        _param.TaskId = 0;
        _param.BasePriority = IRTWorkQueue.TaskPriority.NORMAL;
    }

    public IRTWorkQueue CreatePlatformWorkQueue(
        string usageClass = "",
        IRTWorkQueue.TaskPriority basePriority = IRTWorkQueue.TaskPriority.NORMAL,
        int taskId = 0
    )
    {
        CheckShutdown();

        return new RTWorkQueue(
            _loggerFactory,
            this,
            usageClass,
            basePriority,
            taskId
        );
    }

    public IRTWorkQueue CreatePrivateWorkQueue(
        IRTWorkQueue.WorkQueueType type
    )
    {
        CheckShutdown();

        return new RTWorkQueue(
            _loggerFactory,
            this,
            type
        );
    }

    public IRTWorkQueue CreateSerialWorkQueue(
        IRTWorkQueue workQueue
    )
    {
        CheckShutdown();

        return new RTWorkQueue(
            _loggerFactory,
            this,
            (workQueue as RTWorkQueue)!
        );
    }

    [SupportedOSPlatform("windows")]
    public class RTWorkQueuePeriodicCallback : IDisposable
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private bool _disposed;

        private readonly Action _action;

        private readonly PinnedDelegate<RTWorkQ.RtwqPeriodicCallback> _callBack;
        private readonly nint _thisIUnkown;
        private readonly RTWorkQ.PeriodicCallbackKey _periodicCallbackKey;

        internal RTWorkQueuePeriodicCallback(
            ILoggerFactory loggerFactory,
            Action action
        )
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<RTWorkQueuePeriodicCallback>();
            _action = action;

            _callBack = new PinnedDelegate<RTWorkQ.RtwqPeriodicCallback>(new(PeriodicCallback));
            _thisIUnkown = Marshal.GetIUnknownForObject(this);

            //TODO 間隔を指定する手段が無い？
            Marshal.ThrowExceptionForHR(RTWorkQ.RtwqAddPeriodicCallback(
                _callBack.FunctionPointer,
                _thisIUnkown,
                out _periodicCallbackKey
            ));
        }

        ~RTWorkQueuePeriodicCallback()
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
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
            }

            if (_periodicCallbackKey != default)
            {
                if (
                    !_periodicCallbackKey.IsInvalid
                    && !_periodicCallbackKey.IsClosed
                )
                {
                    //ここに到達するまで動き続けている
                    _periodicCallbackKey.Close();
                }
            }

            {
                var count = Marshal.Release(_thisIUnkown);
                _logger.LogInformation($"this count {count}");
            }

            _callBack?.Dispose();

            _disposed = true;
            _logger.LogInformation("Dispose");
        }

        private void PeriodicCallback(
            object /*IUnknown* */ context
        )
        {
            _action();
        }
    }

    public RTWorkQueuePeriodicCallback AddPeriodicCallback(
        Action action    
    )
    {
        CheckShutdown();

        return
            new RTWorkQueuePeriodicCallback(
                _loggerFactory,
                action
            );
    }

    public void PutWaitingWorkItem(
        IRTWorkQueue.TaskPriority priority,
        WaitHandle waitHandle,
        Action action,
        Action<Exception?, CancellationToken>? afterAction = default,
        CancellationToken ct = default
    )
    {
        var asyncResult = GetAsyncResult(0, RTWorkQ.WorkQueueId.None, action, afterAction);
        try
        {
            _logger.LogTrace($"RtwqPutWaitingWorkItem {asyncResult.Id}.");
            asyncResult.WaitingToRun();
            Marshal.ThrowExceptionForHR(RTWorkQ.RtwqPutWaitingWorkItem(
                waitHandle.SafeWaitHandle,
                (RTWorkQ.AVRT_PRIORITY)priority,
                asyncResult.RtwqAsyncResult,
                out var key
            ));
            _logger.LogTrace($"RtwqPutWaitingWorkItem {asyncResult.Id} {key.Key} ok.");

            asyncResult.BindCancellationToken(key, ct);
        }
        catch
        {
            ReleaseAsyncResult(asyncResult);
            throw;
        }
    }

    public Task PutWaitingWorkItemAsync(
        IRTWorkQueue.TaskPriority priority,
        WaitHandle waitHandle,
        Action action,
        CancellationToken ct
    )
    {
        return ToAsync(
            (afterAction_) => {
                PutWaitingWorkItem(priority, waitHandle, action, afterAction_, ct);
            }
        );
    }

    internal Task ToAsync(
        Action<Action<Exception?, CancellationToken>?> action
    )
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);

        var afterAction_ = (Exception? error, CancellationToken ct) =>
        {
            if (error != null)
            {
                _logger.LogTrace("SetException");
                tcs.SetException(error);
            }
            else if (ct.IsCancellationRequested)
            {
                _logger.LogTrace("SetCanceled");
                tcs.SetCanceled(ct);
            }
            else
            {
                _logger.LogTrace("SetResult");
                tcs.SetResult();
            }
        };

        action(afterAction_);

        return tcs.Task;
    }

    public void ScheduleWorkItem(
    long timeout,
        Action action,
        Action<Exception?, CancellationToken>? afterAction = default,
        CancellationToken ct = default
    )
    {
        var asyncResult = GetAsyncResult(0, RTWorkQ.WorkQueueId.None, action, afterAction);
        try
        {
            _logger.LogTrace($"RtwqScheduleWorkItem {asyncResult.Id}.");
            asyncResult.WaitingToRun();
            Marshal.ThrowExceptionForHR(RTWorkQ.RtwqScheduleWorkItem(
                asyncResult.RtwqAsyncResult,
                timeout,
                out var key
            ));

            asyncResult.BindCancellationToken(key, ct);
        }
        catch
        {
            ReleaseAsyncResult(asyncResult);
            throw;
        }
    }

    public Task ScheduleWorkItemAsync(
        long timeout,
        Action action,
        CancellationToken ct
    )
    {
        return ToAsync(
            (afterAction_) => {
                ScheduleWorkItem(timeout, action, afterAction_, ct);
            }
        );
    }
}
