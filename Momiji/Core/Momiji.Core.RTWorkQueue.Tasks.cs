using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Threading;

namespace Momiji.Core.RTWorkQueue.Tasks;
public class RTWorkQueueTaskSchedulerManagerException : Exception
{
    public RTWorkQueueTaskSchedulerManagerException(string message) : base(message)
    {
    }

    public RTWorkQueueTaskSchedulerManagerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class RTWorkQueueTaskSchedulerManager : IRTWorkQueueTaskSchedulerManager
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RTWorkQueueTaskSchedulerManager> _logger;

    private readonly ConcurrentDictionary<RTWorkQueueTaskScheduler.Key, RTWorkQueueTaskScheduler> _taskSchedulerMap = new();

    private bool _disposed;
    private bool _shutdown;

    public RTWorkQueueTaskSchedulerManager(
        IConfiguration configuration,
        ILoggerFactory loggerFactory
    )
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<RTWorkQueueTaskSchedulerManager>();
    }

    ~RTWorkQueueTaskSchedulerManager()
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
            foreach (var (_, taskScheduler) in _taskSchedulerMap)
            {
                taskScheduler.Dispose();
            }
            _taskSchedulerMap.Clear();
        }

        _disposed = true;
        _logger.LogTrace("Dispose");
    }

    private void CheckShutdown()
    {
        if (_shutdown)
        {
            throw new InvalidOperationException("in shutdown.");
        }
    }

    public TaskScheduler GetTaskScheduler(
        string usageClass = "",
        IRTWorkQueue.WorkQueueType? type = null,
        bool serial = false,
        IRTWorkQueue.TaskPriority basePriority = IRTWorkQueue.TaskPriority.NORMAL,
        int taskId = 0
    )
    {
        CheckShutdown();

        var key =
            new RTWorkQueueTaskScheduler.Key(
                usageClass,
                type,
                serial,
                basePriority,
                taskId
        );

        //valueFactory は多重に実行される可能性があるが、RTWorkQueueTaskSchedulerを作っただけではリソース確保はしないので、良しとする
        return _taskSchedulerMap.GetOrAdd(key, (key) => {
            return 
                new RTWorkQueueTaskScheduler(
                    _configuration,
                    _loggerFactory,
                    key
                );
        });
    }

    public void ShutdownTaskScheduler(
        TaskScheduler taskScheduler
    )
    {
        if (taskScheduler is not RTWorkQueueTaskScheduler target)
        {
            throw new RTWorkQueueTaskSchedulerManagerException("invalid taskScheduler.");
        }

        if (_taskSchedulerMap.TryRemove(target.SelfKey, out var _))
        {
            target.Dispose();
        }
        else
        {
            throw new RTWorkQueueTaskSchedulerManagerException("not found taskScheduler.");
        }
    }
}

internal class RTWorkQueueTaskScheduler : TaskScheduler, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RTWorkQueueTaskScheduler> _logger;

    private readonly WorkQueues _workQueues = new();

    internal record class Key(
        string UsageClass,
        IRTWorkQueue.WorkQueueType? Type,
        bool Serial,
        IRTWorkQueue.TaskPriority BasePriority,
        int TaskId
    );

    private readonly Key _key;
    internal Key SelfKey => _key;

    private class WorkQueues
    {
        public IRTWorkQueueManager? workQueueManager;
        public IRTWorkQueue? workQueue;
        public IRTWorkQueue? workQueueForLongRunning;
    }

    private bool _disposed;
    private bool _shutdown;

    public RTWorkQueueTaskScheduler(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        Key key
    )
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<RTWorkQueueTaskScheduler>();
        _key = key;
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
            _logger.LogDebug($"Disposed {_key}");
            return;
        }

        _logger.LogTrace($"Dispose start {_key}");

        if (disposing)
        {
            lock (_workQueues)
            {
                _workQueues.workQueueForLongRunning?.Dispose();
                _workQueues.workQueue?.Dispose();
                _workQueues.workQueueManager?.Dispose();
            }
        }

        _disposed = true;
        _logger.LogTrace($"Dispose {_key}");
    }

    private void CheckShutdown()
    {
        if (_shutdown)
        {
            throw new InvalidOperationException("in shutdown.");
        }
    }

    protected override IEnumerable<Task>? GetScheduledTasks() => throw new NotImplementedException();

    private IRTWorkQueue GetWorkQueue(bool longRunning)
    {
        var workQueues = _workQueues;
        var workQueueManager = workQueues.workQueueManager;
        if (workQueueManager == null)
        {
            lock (workQueues)
            {
                if (workQueueManager == null)
                {
                    workQueueManager = MTAExecuter.Invoke(_logger, () => { 
                        return new RTWorkQueueManager(_configuration, _loggerFactory);
                    });

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
                    if (_key.Type != null)
                    {
                        workQueue = MTAExecuter.Invoke(_logger, () => {
                            return workQueueManager.CreatePrivateWorkQueue((IRTWorkQueue.WorkQueueType)_key.Type);
                        });
                    }
                    else
                    {
                        workQueue = MTAExecuter.Invoke(_logger, () => {
                            return workQueueManager.CreatePlatformWorkQueue(_key.UsageClass, _key.BasePriority, _key.TaskId);
                        });
                    }

                    //TODO serial

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
        CheckShutdown();

        _logger.LogTrace($"QueueTask {task.Id} {task.CreationOptions}");

        var workQueue = GetWorkQueue(((task.CreationOptions & TaskCreationOptions.LongRunning) != 0));

        //TODO IContextCallbackのキャプチャは要る？
        MTAExecuter.Invoke(_logger, () => {
            workQueue.PutWorkItem(
                IRTWorkQueue.TaskPriority.NORMAL,
                () => {
                    TryExecuteTask(task);
                }
            );
        });
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
