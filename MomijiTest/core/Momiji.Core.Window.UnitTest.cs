using Microsoft.Extensions.Logging;
using Xunit;

namespace Momiji.Core.Window;

public class WindowExceptionUnitTest
{
    [Fact]
    public void Test1()
    {
        var test = new WindowException("test1");
        Assert.NotNull(test.Message);
    }

    [Fact]
    public void Test2()
    {
        var test = new WindowException("test2", new Exception("inner"));
        Assert.NotNull(test.Message);
    }
}

public class WindowUnitTest
{
    [Fact]
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
        window.Move(0, 0, 100, 100, true);
        window.Show(1);
        window.Move(100, 100, 100, 100, true);
        window.Move(200, 200, 200, 200, true);
        window.Show(0);

        window.SetWindowStyle(0);

        {
            var result = window.Dispatch(() => { 
                //immidiate mode
                return window.SetWindowStyle(0); 
            });
            Assert.True(result);
        }

        {
            var result = window.Dispatch(() => { return 999; });
            Assert.Equal(999, result);
        }

        {
            var result = window.Dispatch(() => {
                return window.Dispatch(() =>
                { //re-entrant
                    return 888;
                }); 
            });
            Assert.Equal(888, result);
        }

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

}
