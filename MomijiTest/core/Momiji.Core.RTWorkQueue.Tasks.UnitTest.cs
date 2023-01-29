using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Momiji.Core.Timer;

namespace Momiji.Core.RTWorkQueue.Tasks;

[TestClass]
public class RTWorkQueueTasksTest
{
    private const int TIMES = 500;
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
    private static ILogger<RTWorkQueueTasksTest>? _logger;
    private static RTWorkQueuePlatformEventsHandler? _workQueuePlatformEventsHandler;
    //private static RTWorkQueueManager? _workQueueManager;
    private static RTWorkQueueTaskScheduler? _workQueueTaskScheduler;

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
            builder.AddFilter("Momiji.Internal.Debug", LogLevel.Information);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });
        _logger = _loggerFactory.CreateLogger<RTWorkQueueTasksTest>();

        _workQueuePlatformEventsHandler = new(_loggerFactory);
        //_workQueueManager = new(configuration, _loggerFactory);
        _workQueueTaskScheduler = new(configuration, _loggerFactory);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _loggerFactory?.Dispose();
        _workQueueTaskScheduler?.Dispose();
        //_workQueueManager?.Dispose();
        _workQueuePlatformEventsHandler?.Dispose();
    }

    private static int TestTask(
        ElapsedTimeCounter counter,
        ConcurrentQueue<(string, long)> list,
        int[] result,
        int index,
        int value,
        CountdownEvent? cde = default
    )
    {
        list.Enqueue(($"action invoke {index} {value}", counter.ElapsedTicks));
        Thread.CurrentThread.Join(WAIT);
        result[index] = value;

        if (cde != null)
        {
            if (!cde.IsSet)
            {
                cde.Signal();
            }
        }
        list.Enqueue(($"action end {index}", counter.ElapsedTicks));
        return value;
    }

    private static void PrintResult(
        ConcurrentQueue<(string, long)> list,
        int[] result
    )
    {
        for (var index = 0; index < result.Length; index++)
        {
            Assert.AreEqual(index+1, result[index]);
        }

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
    public void TestLoopNormal()
    {
        var list = new ConcurrentQueue<(string, long)>();
        var result = new int[TIMES];

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        for (var index = 0; index < TIMES; index++)
        {
            list.Enqueue(($"action{index} put", counter.ElapsedTicks));
            TestTask(counter, list, result, index, index+1);
        }

        PrintResult(list, result);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TestLoopParallel(bool rtwqTaskScheduler)
    {
        var list = new ConcurrentQueue<(string, long)>();
        var result = new int[TIMES];

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        var options = new ParallelOptions
        {
            TaskScheduler = rtwqTaskScheduler ? _workQueueTaskScheduler : TaskScheduler.Default
        };

        Parallel.For(0, TIMES, options, (index) => {
            list.Enqueue(($"action put {index}", counter.ElapsedTicks));
            TestTask(counter, list, result, index, index + 1);
        });

        PrintResult(list, result);
    }

    [DataTestMethod]
    [DataRow(false, ApartmentState.STA, ApartmentState.STA)]
    [DataRow(true, ApartmentState.STA, ApartmentState.STA)]
    [DataRow(false, ApartmentState.MTA, ApartmentState.STA)]
    [DataRow(true, ApartmentState.MTA, ApartmentState.STA)]
    [DataRow(false, ApartmentState.STA, ApartmentState.MTA)]
    [DataRow(true, ApartmentState.STA, ApartmentState.MTA)]
    [DataRow(false, ApartmentState.MTA, ApartmentState.MTA)]
    [DataRow(true, ApartmentState.MTA, ApartmentState.MTA)]
    public void TestApartment(
        bool rtwqTaskScheduler, 
        ApartmentState apartmentState1, 
        ApartmentState apartmentState2
    )
    {
        var configuration = CreateConfiguration();
        var scheduler = rtwqTaskScheduler ? _workQueueTaskScheduler : TaskScheduler.Default;

        var thread = new Thread(() => {
            var factory = new TaskFactory(scheduler);

            var task = 
                factory.StartNew(() => {
                    _logger!.LogInformation("task 1");
                });
            task.Wait();

            {
                var thread = new Thread(() => {
                    var task =
                        factory.StartNew(() => {
                            _logger!.LogInformation("task 2");
                        });
                    task.Wait();
                });
                thread.TrySetApartmentState(apartmentState2);
                thread.Start();
                thread.Join();
            }
        });
        thread.TrySetApartmentState(apartmentState1);
        thread.Start();
        thread.Join();
    }

    [DataTestMethod]
    [DataRow(false, TIMES)]
    [DataRow(true, TIMES)]
    [DataRow(false, 1)]
    [DataRow(true, 1)]
    public void TestDataflow(bool rtwqTaskScheduler, int maxDegreeOfParallelism)
    {
        var list = new ConcurrentQueue<(string, long)>();
        var result = new int[TIMES];

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        var options = new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            TaskScheduler = rtwqTaskScheduler ? _workQueueTaskScheduler! : TaskScheduler.Default
        };

        var block = new TransformBlock<int, int>(index => {
            TestTask(counter, list, result, index, index + 1);
            return index;
        }, options);

        var next = new TransformBlock<int, int>(index => {
            TestTask(counter, list, result, index, index + 1);
            return index;
        }, options);

        var last = new ActionBlock<int>(index => {
            TestTask(counter, list, result, index, index + 1);
        }, options);

        block.LinkTo(next);
        next.LinkTo(last);

        for (var index = 0; index < TIMES; index++)
        {
            list.Enqueue(($"action put {index}", counter.ElapsedTicks));
            block.Post(index);
        }
        block.Complete();

        //I—¹‘Ò‚¿
        block.Completion.Wait();

        PrintResult(list, result);
    }

    [TestMethod]
    public void TestThreadPool_QueueUserWorkItem()
    {
        var list = new ConcurrentQueue<(string, long)>();
        var result = new int[TIMES];

        using var cde = new CountdownEvent(TIMES);

        var counter = new ElapsedTimeCounter();
        counter.Reset();

        var workItem = (int index) =>
        {
            TestTask(counter, list, result, index, index+1, cde);
        };

        for (var i = 0; i < TIMES; i++)
        {
            var j = i;
            list.Enqueue(($"action put {j}", counter.ElapsedTicks));
            ThreadPool.QueueUserWorkItem(workItem, j, false);
        }

        //I—¹‘Ò‚¿
        cde.Wait();

        PrintResult(list, result);
    }

    [DataTestMethod]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.None, TaskContinuationOptions.None, false)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.None, TaskContinuationOptions.None, true)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.None, TaskContinuationOptions.None, false, true)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.None, TaskContinuationOptions.None, true, true)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.AttachedToParent, false)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.AttachedToParent, true)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.ExecuteSynchronously, false)]
    [DataRow(TaskCreationOptions.None, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.ExecuteSynchronously, true)]
    [DataRow(TaskCreationOptions.DenyChildAttach, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.AttachedToParent, false)]
    [DataRow(TaskCreationOptions.DenyChildAttach, TaskCreationOptions.AttachedToParent, TaskContinuationOptions.AttachedToParent, true)]
    [DataRow(TaskCreationOptions.LongRunning, TaskCreationOptions.LongRunning, TaskContinuationOptions.LongRunning, false)]
    [DataRow(TaskCreationOptions.LongRunning, TaskCreationOptions.LongRunning, TaskContinuationOptions.LongRunning, true)]
    public async Task TestTaskFactoryStartNewAction(
        TaskCreationOptions taskCreationOptionsParent,
        TaskCreationOptions taskCreationOptionsChild,
        TaskContinuationOptions taskContinuationOptions,
        bool rtwqTaskScheduler,
        bool childError = false
    )
    {
        var scheduler = rtwqTaskScheduler ? _workQueueTaskScheduler : TaskScheduler.Default;
        var factory =
            new TaskFactory(
                CancellationToken.None,
                taskCreationOptionsParent,
                taskContinuationOptions,
                scheduler
            );

        var result = new int[3];
        Assert.AreEqual(0, result[0]);
        Assert.AreEqual(0, result[1]);
        Assert.AreEqual(0, result[2]);

        Task? child = null;
        Task? cont = null;

        var parent = factory.StartNew(() => {

            child = factory.StartNew(() => {
                Task.Delay(100).Wait();
                _logger?.LogInformation($"2:{Environment.StackTrace}");

                if (childError)
                {
                    throw new Exception("child error");
                }

                result[1] = 2;
            }, taskCreationOptionsChild);

            cont = child.ContinueWith((task) => {
                Task.Delay(200).Wait();
                _logger?.LogInformation($"3:{Environment.StackTrace}");

                result[2] = 3;
            }, taskContinuationOptions);

            _logger?.LogInformation($"1:{Environment.StackTrace}");
            result[0] = 1;
        });

        await parent;
        _logger?.LogInformation($"Parent end {taskCreationOptionsParent} {taskCreationOptionsChild} {taskContinuationOptions}");

        Assert.AreEqual(1, result[0]);

        _logger?.LogInformation($"Parent !DenyChildAttach {((taskCreationOptionsParent & TaskCreationOptions.DenyChildAttach) == 0)}");
        _logger?.LogInformation($"Child AttachedToParent {((taskCreationOptionsChild & TaskCreationOptions.AttachedToParent) != 0)}");

        if (
            ((taskCreationOptionsParent & TaskCreationOptions.DenyChildAttach) == 0)
            && ((taskCreationOptionsChild & TaskCreationOptions.AttachedToParent) != 0)
        )
        {
            Assert.AreEqual(2, result[1]);
        }
        else
        {
            try
            {
                await child!;
                Assert.AreEqual(2, result[1]);
            }
            catch(Exception)
            {
                if (!childError)
                {
                    Assert.Fail();
                }
            }
        }

        _logger?.LogInformation($"Continue AttachedToParent {((taskContinuationOptions & TaskContinuationOptions.AttachedToParent) != 0)}");

        if (
            ((taskCreationOptionsParent & TaskCreationOptions.DenyChildAttach) == 0)
            && ((taskContinuationOptions & TaskContinuationOptions.AttachedToParent) != 0)
        )
        {
            Assert.AreEqual(3, result[2]);
        }
        else
        {
            await cont!;
            Assert.AreEqual(3, result[2]);
        }
    }

    [DataTestMethod]
    [DataRow(false, TaskCreationOptions.None)]
    [DataRow(true, TaskCreationOptions.None)]
    [DataRow(false, TaskCreationOptions.AttachedToParent)]
    [DataRow(true, TaskCreationOptions.AttachedToParent)]
    public async Task TestTaskFactoryStartNewAction2(
        bool rtwqTaskScheduler,
        TaskCreationOptions taskCreationOptionsParent
    )
    {
        var scheduler = rtwqTaskScheduler ? _workQueueTaskScheduler : TaskScheduler.Default;
        var factory = new TaskFactory(CancellationToken.None, taskCreationOptionsParent, TaskContinuationOptions.None, scheduler);

        _logger?.LogInformation("START 1");
        await factory.StartNew(async () => {
            _logger?.LogInformation("START 2");
            await factory.StartNew(async () => {
                _logger?.LogInformation("START 3");
                await factory.StartNew(async () => {
                    await Task.Delay(1);
                });
                _logger?.LogInformation("END 3");
            });
            _logger?.LogInformation("END 2");
        });
        _logger?.LogInformation("END 1");
    }
}
