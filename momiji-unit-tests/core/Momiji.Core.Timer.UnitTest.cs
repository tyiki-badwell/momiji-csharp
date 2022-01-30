using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Xunit;

namespace Momiji.Core.Timer
{
    public class WaiterTest
    {
        [Fact]
        public void Test1()
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddFilter("Momiji", LogLevel.Debug);
                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
                builder.AddConsole();
                builder.AddDebug();
            });

            var log = loggerFactory.CreateLogger("WaiterTest");

            var counter = new ElapsedTimeCounter();

            var start = counter.NowTicks;
            var sample = 1;
            var interval = 10_000 * sample;

            var list = new List<(int, long, double, double)>();

            using var waitableTimer = new Waiter(counter, interval, true);
            counter.Reset();

            for (int i = 0; i < 100; i++)
            {
                var before = counter.NowTicks - start;
                var r = waitableTimer.Wait();
                var after = counter.NowTicks - start;
                while(--r > 1)
                {
                    list.Add((i++, default, default, default));
                }

                list.Add((i, waitableTimer.BeforeFlames, before, after));
            }

            foreach (var (i, laps, before, after) in list)
            {
                log.LogInformation($"{i}\t{laps}\t{before}\t{before - ((i + 1) * interval)}\t{after}\t{after - ((i+1) * interval)}\t{after-before}");
            }
        }
    }

}
