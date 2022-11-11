using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace Momiji.Core.Window;

[TestClass]
public class WindowExceptionUnitTest
{
    [TestMethod]
    public void Test1()
    {
        var test = new WindowException("test1");
        Assert.IsNotNull(test.Message);
    }

    [TestMethod]
    public void Test2()
    {
        var test = new WindowException(1400, "test2");
        Assert.IsNotNull(test.Message);
    }

    [TestMethod]
    public void Test3()
    {
        var test = new WindowException("test2", new Exception("inner"));
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

        var logger = loggerFactory.CreateLogger<WindowUnitTest>();

        using var tokenSource = new CancellationTokenSource();

        using var manager = new WindowManager(loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var window = manager.CreateWindow();
        window.Show(1);
        window.Move(0, 0, 100, 100, true);
        window.Move(100, 100, 100, 100, true);
        window.Move(200, 200, 200, 200, true);
        window.Show(0);

        window.SetWindowStyle(0);

        window.Dispatch(() => { return 0; });

        window.Close();

        try
        {
            window.Show(1);
            Assert.Fail("ÉGÉâÅ[Ç™ãNÇ´Ç»Ç©Ç¡ÇΩ");
        }
        catch (Exception e)
        {
            logger.LogInformation(e, "show failed.");
        }

        tokenSource.Cancel();
        task.Wait();
    }

    /*
    [TestMethod]
    public void Test2()
    {
        Bootstrap.Initialize(0x00010001);

        Bootstrap.Shutdown();
    }
    */
}
