using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Windowing;

namespace Momiji.WinRT.Window;

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

        var window = AppWindow.Create();
        window.Show();

        Task.Delay(1000).Wait();
    }
}
