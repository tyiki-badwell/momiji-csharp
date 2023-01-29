using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Momiji.Core.Timer;

namespace Momiji.Core.RTWorkQueue;

[TestClass]
public class RTWorkQueueTest
{
    private const int TIMES = 100;
    private const int WAIT = 10;

    private static IConfiguration CreateConfiguration(string usageClass = "", int basePriority = 0, int taskId = 0)
    {
        var configuration =
            new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var section = configuration.GetSection("Momiji.Core.WorkQueue.WorkQueueManager");

        if (usageClass != "")
        {
            section["UsageClass"] = usageClass;
        }

        if (basePriority != 0)
        {
            section["BasePriority"] = basePriority.ToString();
        }

        if (taskId != 0)
        {
            section["TaskId"] = taskId.ToString();
        }

        return configuration;
    }

    private static ILoggerFactory? _loggerFactory;
    private static ILogger<RTWorkQueueTest>? _logger;
    private static RTWorkQueuePlatformEventsHandler? _workQueuePlatformEventsHandler;
    private static RTWorkQueueManager? _workQueueManager;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        var configuration = CreateConfiguration(/*usageClass, 0, taskId*/);

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Cache", LogLevel.Information);
            builder.AddFilter("Momiji.Core.RTWorkQueue", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });
        _logger = _loggerFactory.CreateLogger<RTWorkQueueTest>();

        _workQueuePlatformEventsHandler = new(_loggerFactory);
        _workQueueManager = new (configuration, _loggerFactory);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _loggerFactory?.Dispose();
        _workQueueManager?.Dispose();
        _workQueuePlatformEventsHandler?.Dispose();
    }

    private static T TestTask<T>(
        ElapsedTimeCounter counter,
        ConcurrentQueue<(string, long)> list,
        T id,
        CountdownEvent? cde = default
    )
    {
        list.Enqueue(($"action invoke {id}", counter.ElapsedTicks));
        Thread.CurrentThread.Join(WAIT);
        if (cde != null)
        {
            if (!cde.IsSet)
            {
                cde.Signal();
            }
        }
        list.Enqueue(($"action end {id}", counter.ElapsedTicks));
        return id;
    }

    private static void PrintResult(
        ConcurrentQueue<(string, long)> list
    )
    {
        {
            var (tag, time) = list.ToList()[^1];
            _logger?.LogInformation($"LAST: {tag}\t{(double)time / 10000}");
        }

        foreach (var (tag, time) in list)
        {
            _logger?.LogInformation($"{tag}\t{(double)time / 10000}");
        }
    }

    [TestMethod]
    public void TestRtwqStartupTwice()
    {
        var configuration = CreateConfiguration();


        using var workQueueManager = new RTWorkQueueManager(configuration, _loggerFactory!);
        using var workQueueManager2 = new RTWorkQueueManager(configuration, _loggerFactory!);


    }

    [TestMethod]
    public void TestRtwqCreateWorkQueue()
    {
        var usageClass = "Pro Audio";
        var basePriority = IRTWorkQueue.TaskPriority.CRITICAL;
        var taskId = 0;

        using var workQueue = _workQueueManager!.CreatePlatformWorkQueue(usageClass, basePriority);

        Assert.AreEqual(usageClass, workQueue.GetMMCSSClass());
        Assert.AreEqual(basePriority, workQueue.GetMMCSSPriority());
        Assert.AreNotEqual(taskId, workQueue.GetMMCSSTaskId());
    }

    [DataTestMethod]
    [DataRow("Audio")]
    [DataRow("Capture")]
    [DataRow("DisplayPostProcessing", null, false, true)]
    [DataRow("Distribution")]
    [DataRow("Games")]
    [DataRow("Playback")]
    [DataRow("Pro Audio")]
    [DataRow("Window Manager")]
    [DataRow("Audio", null, true)]
    [DataRow("Capture", null, true)]
    [DataRow("DisplayPostProcessing", null, true, true)]
    [DataRow("Distribution", null, true)]
    [DataRow("Games", null, true)]
    [DataRow("Playback", null, true)]
    [DataRow("Pro Audio", null, true)]
    [DataRow("Window Manager", null, true)]
    [DataRow("", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("", IRTWorkQueue.WorkQueueType.Window)]
    [DataRow("", IRTWorkQueue.WorkQueueType.MultiThreaded)]
    [DataRow("", IRTWorkQueue.WorkQueueType.Standard, true)]
    [DataRow("", IRTWorkQueue.WorkQueueType.Window, true, true)] //windowÇÃserialÇÕNGÇÁÇµÇ¢
    [DataRow("", IRTWorkQueue.WorkQueueType.MultiThreaded, true)]
    public async Task TestRtwqAsync(
        string usageClass,
        IRTWorkQueue.WorkQueueType? type = null,
        bool serial = false,
        bool argumentException = false
    )
    {
        IRTWorkQueue workQueue;
        IRTWorkQueue? workQueueOrg = null;
        if (type != null)
        {
            workQueue = _workQueueManager!.CreatePrivateWorkQueue((IRTWorkQueue.WorkQueueType)type);
        }
        else
        {
            try
            {
                workQueue = _workQueueManager!.CreatePlatformWorkQueue(usageClass);
            }
            catch (ArgumentException)
            {
                if (argumentException)
                {
                    return;
                }
                throw;
            }
        }

        try
        {
            if (serial)
            {
                workQueueOrg = workQueue;
                try
                {
                    workQueue = _workQueueManager!.CreateSerialWorkQueue(workQueueOrg);
                }
                catch (ArgumentException)
                {
                    if (argumentException)
                    {
                        return;
                    }
                    throw;
                }
            }

            {
                //ÇPâÒñ⁄ÇÃãNìÆÇ™íxÇ¢ÇÃÇ≈ÅAãÛë≈ÇøÇ∑ÇÈ
                _logger?.LogInformation("dummy action start.");
                var task = workQueue.PutWorkItemAsync(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        _logger?.LogInformation("dummy invoke.");
                    }
                );
                await task;

                _logger?.LogInformation($"dummy action end task IsCanceled:{task.IsCanceled} IsFaulted:{task.IsFaulted} IsCompletedSuccessfully:{task.IsCompletedSuccessfully}");
            }
            var list = new ConcurrentQueue<(string, long)>();
            var taskSet = new HashSet<Task>();
            var counter = new ElapsedTimeCounter();
            counter.Reset();
            for (var i = 1; i <= TIMES; i++)
            {
                var j = i;
                list.Enqueue(($"action put {i}", counter.ElapsedTicks));
                taskSet.Add(workQueue.PutWorkItemAsync(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        TestTask(counter, list, j);
                    }
                ));
            }

            //èIóπë“Çø
            while (taskSet.Count > 0)
            {
                var task = await Task.WhenAny(taskSet).ConfigureAwait(false);
                taskSet.Remove(task);

                _logger?.LogDebug(($"task IsCanceled:{task.IsCanceled} IsFaulted:{task.IsFaulted} IsCompletedSuccessfully:{task.IsCompletedSuccessfully}"));
            }

            PrintResult(list);
        }
        finally
        {
            workQueue.Dispose();

            if (serial)
            {
                workQueueOrg?.Dispose();
            }
        }
    }

    [DataTestMethod]
    [DataRow(null, null, false, true)] //usage classÇ…nullÇÕNG
    [DataRow("")] //shared queue Ç""Ç≈çÏê¨Ç∑ÇÈÇÃÇÕregular-priority queueÇçÏÇÈì¡éÍÇ»ìÆçÏÇ…Ç»Ç¡ÇƒÇ¢ÇÈ
    [DataRow("Audio")]
    [DataRow("Capture")]
    [DataRow("DisplayPostProcessing", null, false, true)] //èâÇﬂÇ©ÇÁDisplayPostProcessingÇÕé∏îsÇ∑ÇÈ
    [DataRow("Distribution")]
    [DataRow("Games")]
    [DataRow("Playback")]
    [DataRow("Pro Audio")]
    [DataRow("Window Manager")]
    [DataRow("****", null, false, true)] //é∏îsÇ∑ÇÈ
    [DataRow("Audio", null, true)]
    [DataRow("Capture", null, true)]
    [DataRow("DisplayPostProcessing", null, true, true)] //é∏îsÇ∑ÇÈ
    [DataRow("Distribution", null, true)]
    [DataRow("Games", null, true)]
    [DataRow("Playback", null, true)]
    [DataRow("Pro Audio", null, true)]
    [DataRow("Window Manager", null, true)]
    [DataRow(null, IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("", IRTWorkQueue.WorkQueueType.Standard, false, true)] //private queueÇ""Ç…ìoò^Ç∑ÇÈÇ∆é∏îsÇ∑ÇÈ
    [DataRow("Audio", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("Capture", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("DisplayPostProcessing", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("Distribution", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("Games", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("Playback", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("Pro Audio", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow("Window Manager", IRTWorkQueue.WorkQueueType.Standard)]
    [DataRow(null, IRTWorkQueue.WorkQueueType.Window)]
    [DataRow(null, IRTWorkQueue.WorkQueueType.MultiThreaded)]
    [DataRow(null, IRTWorkQueue.WorkQueueType.Standard, true)]
    [DataRow(null, IRTWorkQueue.WorkQueueType.Window, true, true)]
    [DataRow(null, IRTWorkQueue.WorkQueueType.MultiThreaded, true)]
    public async Task TestRtwq(
        string? usageClass = null,
        IRTWorkQueue.WorkQueueType? type = null,
        bool serial = false,
        bool argumentException = false
    )
    {
        IRTWorkQueue workQueue;
        IRTWorkQueue? workQueueOrg = null;
        if (type != null)
        {
            workQueue = _workQueueManager!.CreatePrivateWorkQueue((IRTWorkQueue.WorkQueueType)type);
        }
        else
        {
            try
            {
                workQueue = _workQueueManager!.CreatePlatformWorkQueue(usageClass!);
                if (argumentException)
                {
                    Assert.Fail();
                }
            }
            catch (COMException)
            {
                if (argumentException)
                {
                    return;
                }
                throw;
            }
            catch (NullReferenceException)
            {
                if (argumentException)
                {
                    return;
                }
                throw;
            }
            catch (ArgumentException)
            {
                if (argumentException)
                {
                    return;
                }
                throw;
            }
        }

        try
        {
            if (serial)
            {
                workQueueOrg = workQueue;
                try
                {
                    workQueue = _workQueueManager!.CreateSerialWorkQueue(workQueueOrg);
                    if (argumentException)
                    {
                        Assert.Fail();
                    }
                }
                catch (ArgumentException)
                {
                    if (argumentException)
                    {
                        return;
                    }
                    throw;
                }
            }

            if ((type != null) && (usageClass != null))
            {//private queueÇÕÅAclassÇÃêÿÇËë÷Ç¶Ç™èoóàÇÈ
                try
                {
                    await workQueue.RegisterMMCSSAsync(usageClass!, IRTWorkQueue.TaskPriority.NORMAL, 0);
                    if (argumentException)
                    {
                        Assert.Fail();
                    }
                }
                catch (COMException)
                {
                    if (argumentException)
                    {
                        return;
                    }
                    throw;
                }

                var afterClass = workQueue.GetMMCSSClass();
                Assert.AreEqual(usageClass, afterClass);
            }

            {
                //ÇPâÒñ⁄ÇÃãNìÆÇ™íxÇ¢ÇÃÇ≈ÅAãÛë≈ÇøÇ∑ÇÈ
                _logger?.LogInformation("dummy action start.");
                workQueue.PutWorkItem(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        _logger?.LogInformation("dummy invoke.");
                    },
                    (error, ct) =>
                    {
                        _logger?.LogInformation($"dummy result error [{error}] ct [{ct.IsCancellationRequested}].");
                    }
                );
                _logger?.LogInformation("dummy action end.");
            }

            var list = new ConcurrentQueue<(string, long)>();

            var counter = new ElapsedTimeCounter();
            counter.Reset();

            for (var i = 1; i <= TIMES; i++)
            {
                var j = i;

                list.Enqueue(($"action put {i}", counter.ElapsedTicks));
                workQueue.PutWorkItem(
                    IRTWorkQueue.TaskPriority.NORMAL,
                    () =>
                    {
                        TestTask(counter, list, j);
                    },
                    (error, ct) =>
                    {
                        list.Enqueue(($"result error [{error}] ct [{ct.IsCancellationRequested}].", counter.ElapsedTicks));
                    }
                );
            }

            PrintResult(list);
        }
        finally
        {
            if (type != null)
            {
                if (usageClass != null && usageClass != "")
                {
                    await workQueue.UnregisterMMCSSAsync();
                }
            }

            workQueue.Dispose();

            if (serial)
            {
                workQueueOrg?.Dispose();
            }
        }
    }

    [TestMethod]
    public void TestWorkQueuePeriodicCallback()
    {
        _workQueueManager!.RegisterMMCSS("Pro Audio");

        using var cde = new CountdownEvent(10);

        var list = new ConcurrentQueue<(string, long)>();

        var counter = new ElapsedTimeCounter();
        counter.Reset();
        {
            var i = 1;

            //15msec Ç≈åƒÇ—èoÇ≥ÇÍÇƒÇ¢ÇÈñÕól
            using var workQueuePeriodicCallback = _workQueueManager!.AddPeriodicCallback(() => {
                list.Enqueue(($"periodic", counter.ElapsedTicks));
                TestTask(counter, list, i++, cde);
            });

            //èIóπë“Çø
            cde.Wait();
        }

        PrintResult(list);
    }

    [DataTestMethod]
    [DataRow("Pro Audio")]
    public async Task TestRtwqPutWaitingAsync_WaitableTimer(string usageClass)
    {
        _workQueueManager!.RegisterMMCSS(usageClass);

        var list = new ConcurrentQueue<(string, long)>();

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        using var timer = new WaitableTimer(false, true);

        for (var i = 1; i <= 10; i++)
        {
            using var cts = new CancellationTokenSource();

            var j = i;
            list.Enqueue(($"action put {j}", counter.ElapsedTicks));

            var task = _workQueueManager!.PutWaitingWorkItemAsync(
                0, 
                timer, 
                () => {
                    TestTask(counter, list, j);
                }, 
                cts.Token
            );

            timer.Set(-10000);

            await task;
            list.Enqueue(($"result {j}", counter.ElapsedTicks));
        }

        PrintResult(list);
    }

    [DataTestMethod]
    [DataRow("Pro Audio")]
    public void TestRtwqPutWaiting_WaitableTimer(string usageClass)
    {
        _workQueueManager!.RegisterMMCSS(usageClass);

        var list = new ConcurrentQueue<(string, long)>();

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        using var cde = new CountdownEvent(TIMES);

        using var timer = new WaitableTimer(false, true);
        timer.Set(-100);

        using var cts = new CancellationTokenSource();
        using var wait = new ManualResetEventSlim();

        list.Enqueue(($"action put", counter.ElapsedTicks));

        var i = 0;
        while (true)
        {
            var j = i++;

            list.Enqueue(($"action put {j}", counter.ElapsedTicks));
            _workQueueManager!.PutWaitingWorkItem(
                0,
                timer,
                () =>
                {
                    TestTask(counter, list, j, cde);
                },
                (error, ct) =>
                {
                    list.Enqueue(($"result error [{error}] ct [{ct.IsCancellationRequested}]", counter.ElapsedTicks));
                    wait.Set();
                },
                cts.Token
            );

            if (cde.IsSet)
            {
                break;
            }

            list.Enqueue(($"timer set {j}", counter.ElapsedTicks));
            timer.Set(-100);
            wait.Wait();
            wait.Reset();
        }

        cts.Cancel();

        PrintResult(list);
    }

    [DataTestMethod]
    [DataRow("Pro Audio", true, false, false, false)] //Fire
    [DataRow("Pro Audio", false, true, false, false)] //Cancel
    [DataRow("Pro Audio", true, true, false, false)] //Fire_Cancel
    [DataRow("Pro Audio", false, false, true, false)] //Already_Cancel
    [DataRow("Pro Audio", true, false, false, true)] //Fire_Error
    public void TestRtwqPutWaiting(
        string usageClass,
        bool fire,
        bool cancel,
        bool alreadyCancel,
        bool error
        )
    {
        _workQueueManager!.RegisterMMCSS(usageClass);

        var list = new ConcurrentQueue<(string, long)>();

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        using var cts = new CancellationTokenSource();
        using var waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);

        if (alreadyCancel)
        {
            _logger?.LogInformation($"already cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        var result = 0;
        Exception? exception = null;
        var ct = CancellationToken.None;

        using var wait = new ManualResetEventSlim();

        _workQueueManager!.PutWaitingWorkItem(
            0,
            waitHandle,
            () => {
                _logger?.LogInformation($"invoke {counter.ElapsedTicks}");

                if (error)
                {
                    throw new Exception("error");
                }

                result = 1;
            },
            (exception_, ct_) =>
            {
                exception = exception_;
                ct = ct_;
                wait.Set();
            },
            cts.Token
        );

        if (cancel)
        {
            Task.Delay(100).Wait();
            _logger?.LogInformation($"cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        if (fire)
        {
            Task.Delay(100).Wait();
            _logger?.LogInformation($"set {counter.ElapsedTicks}");
            waitHandle.Set();
        }

        wait.Wait();

        if (ct.IsCancellationRequested)
        {
            _logger?.LogInformation($"cancel");

            if (!cancel && !alreadyCancel)
            {
                Assert.Fail();
            }
        }
        else if (exception != null)
        {
            _logger?.LogInformation(exception, $"error");

            if (!error)
            {
                Assert.Fail();
            }
        }
        else
        {
            _logger?.LogInformation($"result {result} {counter.ElapsedTicks}");

            if (fire)
            {
                Assert.AreEqual(1, result);
            }
            else
            {
                Assert.Fail();
            }
        }
    }

    [DataTestMethod]
    [DataRow("Pro Audio", true, false, false, false)] //Fire
    [DataRow("Pro Audio", false, true, false, false)] //Cancel
    [DataRow("Pro Audio", true, true, false, false)] //Fire_Cancel
    [DataRow("Pro Audio", false, false, true, false)] //Already_Cancel
    [DataRow("Pro Audio", true, false, false, true)] //Fire_Error
    public async Task TestRtwqPutWaitingAsync(
        string usageClass,
        bool fire,
        bool cancel,
        bool alreadyCancel,
        bool error
        )
    {
        _workQueueManager!.RegisterMMCSS(usageClass);

        var list = new ConcurrentQueue<(string, long)>();

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        using var cts = new CancellationTokenSource();
        using var waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);

        if (alreadyCancel)
        {
            _logger?.LogInformation($"already cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        var result = 0;
        var task = _workQueueManager!.PutWaitingWorkItemAsync(
            0, 
            waitHandle, 
            () => {
                _logger?.LogInformation($"invoke {counter.ElapsedTicks}");

                if (error)
                {
                    throw new Exception("error");
                }

                result = 1;
            },
            cts.Token
        );

        if (cancel)
        {
            await Task.Delay(100);
            _logger?.LogInformation($"cancel {counter.ElapsedTicks}");
            cts.Cancel();
        }

        if (fire)
        {
            await Task.Delay(100);
            _logger?.LogInformation($"set {counter.ElapsedTicks}");
            waitHandle.Set();
        }

        try
        {
            await task;

            _logger?.LogInformation($"result {result} {counter.ElapsedTicks}");

            if (fire)
            {
                Assert.AreEqual(1, result);
            }
            else
            {
                Assert.Fail();
            }
        }
        catch (TaskCanceledException e)
        {
            _logger?.LogInformation(e, $"cancel");

            if (!cancel && !alreadyCancel)
            {
                Assert.Fail();
            }
        }
        catch (Exception e)
        {
            _logger?.LogInformation(e, $"error");

            if (!error)
            {
                Assert.Fail();
            }
        }
    }

    [DataTestMethod]
    [DataRow("Pro Audio")]
    public async Task TestRtwqScheduleAsync(string usageClass)
    {
        _workQueueManager!.RegisterMMCSS(usageClass);

        var list = new ConcurrentQueue<(string, long)>();
        var taskSet = new HashSet<Task>();

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        using var cts = new CancellationTokenSource();

        //åÎç∑ 5msecÇ≠ÇÁÇ¢Å@20msecà»â∫Ç…ÇÕèoóàÇ»Ç¢ñÕól
        for (var i = 1; i <= TIMES; i++)
        {
            var j = i;

            list.Enqueue(($"action put {j}", counter.ElapsedTicks));
            taskSet.Add(_workQueueManager!.ScheduleWorkItemAsync(
                -20, 
                () => {
                    TestTask(counter, list, j);
                },
                cts.Token
            ));

        }

        //cts.Cancel();

        //èIóπë“Çø
        while (taskSet.Count > 0)
        {
            var task = await Task.WhenAny(taskSet).ConfigureAwait(false);
            taskSet.Remove(task);

            _logger?.LogDebug(($"task IsCanceled:{task.IsCanceled} IsFaulted:{task.IsFaulted} IsCompletedSuccessfully:{task.IsCompletedSuccessfully}"));
        }

        PrintResult(list);
    }

    [TestMethod]
    public void TestRegisterPlatformWithMMCSS()
    {
        var usageClass = "Audio";
        _workQueueManager!.RegisterMMCSS(usageClass);

        _workQueueManager!.UnregisterMMCSS();

        usageClass = "Pro Audio";
        _workQueueManager!.RegisterMMCSS(usageClass, IRTWorkQueue.TaskPriority.HIGH, 1);

        usageClass = "Playback";
        _workQueueManager!.RegisterMMCSS(usageClass, IRTWorkQueue.TaskPriority.LOW, 2);
    }

    [TestMethod]
    public async Task TestRegisterWorkQueueWithMMCSS()
    {
        var usageClass = "Audio";

        var workQueue = _workQueueManager!.CreatePlatformWorkQueue(usageClass);
        Assert.AreEqual(usageClass, workQueue.GetMMCSSClass());

        await workQueue.UnregisterMMCSSAsync();

        Assert.AreEqual("", workQueue.GetMMCSSClass());

        usageClass = "Pro Audio";
        await workQueue.RegisterMMCSSAsync(usageClass, IRTWorkQueue.TaskPriority.HIGH, 1);

        Assert.AreEqual(usageClass, workQueue.GetMMCSSClass());
        Assert.AreEqual(IRTWorkQueue.TaskPriority.HIGH, workQueue.GetMMCSSPriority());

    }

}
