using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Dll;
using Momiji.Core.Timer;
using Momiji.Interop.Opus;
using Xunit;

namespace Momiji.Core.Opus;

public class OpusExceptionUnitTest
{
    [Fact]
    public void Test1()
    {
        var test = new OpusException("test");
        Assert.NotNull(test.Message);
    }
}

public class OpusUnitTest
{
    [Fact]
    public void Test1()
    {
        var configuration =
            new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("Momiji", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
            builder.AddConsole();
            builder.AddDebug();
        });
        var counter = new ElapsedTimeCounter();
        using var dllManager = new DllManager(configuration, loggerFactory);

        using var opus = new OpusEncoder(SamplingRate.Sampling48000, Channels.Stereo, loggerFactory, counter);

    }
}
