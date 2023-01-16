using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Momiji.Core.Timer;

[TestClass]
public class WaiterTest
{
    [TestMethod]
    public void Test1_1()
    {
        Test1Impl(false);
    }

    [TestMethod]
    public void Test1_2()
    {
        Test1Impl(true);
    }

    private void Test1Impl(bool highResolution)
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

    [TestMethod]
    public void Test2()
    {
        Test2Async().Wait();
    }


    private async Task Test2Async()
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

    [TestMethod]
    public void Test3()
    {
        Test3Async().Wait();
    }

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
