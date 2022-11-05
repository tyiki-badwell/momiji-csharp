using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Momiji.Core.Window;

[TestClass]
public class WindowExceptionUnitTest
{
    [TestMethod]
    public void Test1()
    {
        var test = new WindowException();
        Assert.IsNotNull(test.Message);
    }
}

[TestClass]
public class WindowUnitTest
{
    [TestMethod]
    public void Test1()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("Momiji", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
            builder.AddConsole();
            builder.AddDebug();
        });

        using var tokenSource = new CancellationTokenSource();

        using var manager = new WindowManager(loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var _ = Task.Delay(1000, CancellationToken.None)
                    .ContinueWith(
                        (task) => { tokenSource.Cancel(); },
                        TaskScheduler.Default
                    );

        task.Wait();
    }
}
