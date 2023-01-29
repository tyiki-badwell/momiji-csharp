using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Debug;
using Ole32 = Momiji.Interop.Ole32.NativeMethods;

namespace Momiji.Core.RTWorkQueue.Tasks;

public class RTWorkQueueTaskScheduler : TaskScheduler, IRTWorkQueueTaskScheduler
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RTWorkQueueTaskScheduler> _logger;

    private readonly WorkQueues _workQueuesMTA = new();
    private readonly ConcurrentDictionary<int, WorkQueues> _workQueuesSTA = new();

    private class WorkQueues
    {
        public IRTWorkQueueManager? workQueueManager;
        public IRTWorkQueue? workQueue;
        public IRTWorkQueue? workQueueForLongRunning;
    }

    private bool _disposed;
    private bool _shutdown;

    public TaskScheduler TaskScheduler => this;

    public RTWorkQueueTaskScheduler(
        IConfiguration configuration,
        ILoggerFactory loggerFactory
    )
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<RTWorkQueueTaskScheduler>();
    }

    ~RTWorkQueueTaskScheduler()
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
            _logger.LogDebug("Disposed");
            return;
        }

        _logger.LogTrace("Dispose start");

        if (disposing)
        {
            lock (_workQueuesMTA)
            {
                _workQueuesMTA.workQueueForLongRunning?.Dispose();
                _workQueuesMTA.workQueue?.Dispose();
                _workQueuesMTA.workQueueManager?.Dispose();
            }

            foreach (var (id, sta) in _workQueuesSTA)
            {
                lock (sta)
                {
                    sta.workQueueForLongRunning?.Dispose();
                    sta.workQueue?.Dispose();
                    sta.workQueueManager?.Dispose();
                }
            }
            _workQueuesSTA.Clear();
        }

        _disposed = true;
        _logger.LogTrace("Dispose");
    }

    protected override IEnumerable<Task>? GetScheduledTasks() => throw new NotImplementedException();

    private IRTWorkQueue GetWorkQueue(bool longRunning)
    {
        var apartmentType = ThreadDebug.GetApartmentType();

        //STAから実行するときは、そのスレッドで作ったQueueとResultを使って処理しないと、0xC0000005が発生する
        var isSTA = (apartmentType.AptType == Ole32.APTTYPE.MAINSTA || apartmentType.AptType == Ole32.APTTYPE.STA);
        WorkQueues? workQueues;
        if (isSTA)
        {
            var threadId = apartmentType.ManagedThreadId;
            _logger.LogTrace($"STA {threadId:X}");
            if (!_workQueuesSTA.TryGetValue(threadId, out workQueues))
            {
                workQueues = _workQueuesSTA.GetOrAdd(threadId, new WorkQueues());
            }
        }
        else
        {
            _logger.LogTrace("MTA");
            workQueues = _workQueuesMTA;
        }

        IRTWorkQueueManager? workQueueManager = workQueues.workQueueManager;
        if (workQueueManager == null)
        {
            lock (workQueues)
            {
                if (workQueueManager == null)
                {
                    workQueueManager = new RTWorkQueueManager(_configuration, _loggerFactory);
                    workQueues.workQueueManager = workQueueManager;
                }
            }
        }

        IRTWorkQueue? workQueue;
        if (longRunning)
        {
            workQueue = workQueues.workQueueForLongRunning;
        }
        else
        {
            workQueue = workQueues.workQueue;
        }

        if (workQueue == null)
        {
            lock (workQueues)
            {
                if (longRunning)
                {
                    workQueue = workQueues.workQueueForLongRunning;
                }
                else
                {
                    workQueue = workQueues.workQueue;
                }

                if (workQueue == null)
                {
                    //TODO STAからQueueを作るとInvokeでAsyncResultを更新できない問題　WorkQueueType.Windowは意味ナシ

                    if (isSTA)
                    {
                        workQueue = workQueueManager.CreatePlatformWorkQueue("Pro Audio", IRTWorkQueue.TaskPriority.NORMAL);
                        //workQueue = workQueueManager.CreatePrivateWorkQueue(IRTWorkQueue.WorkQueueType.Window);
                        //workQueue.RegisterMMCSSAsync("Pro Audio", IRTWorkQueue.TaskPriority.NORMAL, 0).Wait();
                    }
                    else
                    {
                        workQueue = workQueueManager.CreatePlatformWorkQueue("Pro Audio", IRTWorkQueue.TaskPriority.NORMAL);
                    }

                    if (longRunning)
                    {
                        workQueue.SetLongRunning(true);
                        workQueues.workQueueForLongRunning = workQueue;
                    }
                    else
                    {
                        workQueues.workQueue = workQueue;
                    }
                }
            }
        }
        return workQueue;
    }

    protected override void QueueTask(Task task)
    {
        _logger.LogTrace($"QueueTask {task.Id} {task.CreationOptions}");

        if (_shutdown)
        {
        //    throw new InvalidOperationException("in shutdown.");
        }

        var workQueue = GetWorkQueue(((task.CreationOptions & TaskCreationOptions.LongRunning) != 0));
        workQueue.PutWorkItem(
            IRTWorkQueue.TaskPriority.NORMAL,
            () => {
                TryExecuteTask(task);
            }
        );
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) {
        _logger.LogDebug($"TryExecuteTaskInline {task.Id} {taskWasPreviouslyQueued}");

        if (taskWasPreviouslyQueued && !TryDequeue(task))
        {
            return false;
        }

        return TryExecuteTask(task);
    }

    protected override bool TryDequeue(Task task)
    {
        _logger.LogDebug($"TryDequeue {task.Id}");
        //キャンセルは出来ない
        return false;
    }
}
