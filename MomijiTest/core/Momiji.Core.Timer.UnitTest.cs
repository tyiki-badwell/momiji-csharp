using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Momiji.Core.Timer;

public class WaitableTimerTest
{
    private const int TIMES = 100;
    private const int INTERVAL = 5_000_0;

    private static ILoggerFactory CreateLoggerFactory()
    {
        return LoggerFactory.Create(builder =>
        {
            builder.AddFilter("Momiji", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
            builder.AddConsole();
            builder.AddDebug();
        });
    }
    private static void PrintResult(
        ConcurrentQueue<(string, long)> list,
        ILogger logger
    )
    {
        {
            var (tag, time) = list.ToList()[^1];
            logger.LogInformation($"LAST: {tag}\t{(double)time / 10000}");
        }

        foreach (var (tag, time) in list)
        {
            logger.LogInformation($"{tag}\t{(double)time / 10000}");
        }
    }

    [Fact]
    public void Test1_1()
    {
        Test1Impl(true, true);
    }

    [Fact]
    public void Test1_2()
    {
        Test1Impl(true, false);
    }
    [Fact]
    public void Test1_3()
    {
        Test1Impl(false, true);
    }
    [Fact]
    public void Test1_4()
    {
        Test1Impl(false, false);
    }

    private static void Test1Impl(
        bool manualReset,
        bool highResolution
    )
    {
        using var loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger("WaitableTimerTest.Test1");
        var list = new ConcurrentQueue<(string, long)>();

        var counter = new ElapsedTimeCounter();

        using var timer = new WaitableTimer(manualReset, highResolution);

        var dueTime = -INTERVAL;

        for (var i = 0; i < TIMES; i++)
        {
            timer.Set(dueTime);

            var start = counter.ElapsedTicks;

            timer.WaitOne();

            var end = counter.ElapsedTicks;

            list.Enqueue(($"LAP {(double)(end - start) / 10_000}", counter.ElapsedTicks));
        }
        PrintResult(list, logger);
    }

}

public class WaiterTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Test1(bool highResolution)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("Momiji", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
            builder.AddConsole();
            builder.AddDebug();
        });

        var log = loggerFactory.CreateLogger("WaiterTest.Test1");

        var counter = new ElapsedTimeCounter();

        var sample = 1;
        var interval = (long)(10_000 * sample);

        var list = new List<(int, long, double, double, double)>();

        using var waiter = new Waiter(counter, interval, highResolution);
        counter.Reset();

        {
            var before = (double)counter.ElapsedTicks / 10_000;
            for (var i = 0; i < 100; i++)
            {
                var leftTicks = waiter.Wait();

                var after = (double)counter.ElapsedTicks / 10_000;

                list.Add((i, waiter.ProgressedFrames, before, after, (double)leftTicks / 10_000));

                if (leftTicks < 0)
                {
                    i += (int)((leftTicks * -1) / interval);
                }

                before = after;

                //Thread.Sleep(1);
            }
        }

        foreach (var (i, laps, before, after, leftTicks) in list)
        {
            log.LogInformation($"count:{i}\tlaps:{laps}\tbefore:{before}\tafter:{after}\tdiff:{after - before}\t{leftTicks}");
        }
    }

    [Fact]
    public async Task Test2Async()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("Momiji", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
            builder.AddConsole();
            builder.AddDebug();
        });

        var log = loggerFactory.CreateLogger("WaiterTest.Test2");

        var counter = new ElapsedTimeCounter();

        var sample = 2;
        var interval = 10_000 * sample;

        var list = new List<(int, long, double, double)>();

        using var timer = new PeriodicTimer(TimeSpan.FromTicks(interval));
        counter.Reset();

        {
            var before = (double)counter.ElapsedTicks / 10_000;
            for (var i = 0; i < 100; i++)
            {
                await timer.WaitForNextTickAsync();

                var after = (double)counter.ElapsedTicks / 10_000;

                list.Add((i, 0, before, after));
                before = after;
            }
        }

        foreach (var (i, laps, before, after) in list)
        {
            log.LogInformation($"count:{i}\tlaps:{laps}\tbefore:{before}\tafter:{after}\tdiff:{after - before}");
        }

    }

    [Fact]
    public async Task Test3Async()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("Momiji", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
            builder.AddConsole();
            builder.AddDebug();
        });

        var log = loggerFactory.CreateLogger("WaiterTest.Test3");

        var counter = new ElapsedTimeCounter();

        var sample = 2;
        var interval = 1 * sample;

        var list = new List<(int, long, double, double)>();

        using var w = new AutoResetEvent(false);

        {
            var before = (double)counter.ElapsedTicks / 10_000;
            var i = 0;
            var r =
                ThreadPool.RegisterWaitForSingleObject(
                    w,
                    (object? _list, bool t) =>
                    {
                        var after = (double)counter.ElapsedTicks / 10_000;
                        list.Add((i++, 0, before, after));
                        before = after;
                    },
                    null,
                    1,
                    false
                );

            await Task.Delay(1000).ContinueWith((_) => {
                r.Unregister(w);
            });
        }

        foreach (var (i, laps, before, after) in list)
        {
            log.LogInformation($"count:{i}\tlaps:{laps}\tbefore:{before}\tafter:{after}\tdiff:{after - before}");
        }
    }
}
