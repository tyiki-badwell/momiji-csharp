namespace Momiji.Core.RTWorkQueue;

public interface IRTWorkQueuePlatformEventsHandler : IDisposable
{
}

public interface IRTWorkQueueTaskScheduler : IDisposable
{
    public TaskScheduler TaskScheduler
    {
        get;
    }
}

public interface IRTWorkQueueManager : IDisposable
{
    public IRTWorkQueue CreatePlatformWorkQueue(
        string usageClass = "",
        IRTWorkQueue.TaskPriority basePriority = IRTWorkQueue.TaskPriority.NORMAL,
        int taskId = 0
    );
    public IRTWorkQueue CreatePrivateWorkQueue(
        IRTWorkQueue.WorkQueueType type
    );

    public IRTWorkQueue CreateSerialWorkQueue(
        IRTWorkQueue workQueue
    );
}

public interface IRTWorkQueue : IDisposable
{
    public enum TaskPriority : int
    {
        LOW = -1,
        NORMAL = 0,
        HIGH = 1,
        CRITICAL = 2
    }

    public enum WorkQueueType : int
    {
        Standard = 0,
        Window = 1,
        MultiThreaded = 2
    }

    public void PutWorkItem(
        TaskPriority priority,
        Action action,
        Action<Exception?, CancellationToken>? afterAction = default
    );

    public Task PutWorkItemAsync(
        TaskPriority priority,
        Action action
    );

    public int GetMMCSSTaskId();
    public TaskPriority GetMMCSSPriority();
    public string GetMMCSSClass();

    public Task RegisterMMCSSAsync(
        string usageClass,
        TaskPriority basePriority,
        int taskId
    );

    public Task UnregisterMMCSSAsync();

    public void SetLongRunning(bool enable);
}
