using Microsoft.Extensions.Logging;
using Xunit;

namespace Momiji.Internal.Debug;

public class WindowDebugUnitTest
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

        WindowDebug.CheckIntegrityLevel(loggerFactory);
    }

    [Fact]
    public void Test2()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("Momiji", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
            builder.AddConsole();
            builder.AddDebug();
        });

        WindowDebug.CheckDesktop(loggerFactory);
    }
    [Fact]
    public void Test3()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("Momiji", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
            builder.AddConsole();
            builder.AddDebug();
        });

        WindowDebug.CheckGetProcessInformation(loggerFactory);
    }
}
