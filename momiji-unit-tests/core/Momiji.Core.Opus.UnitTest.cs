using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Momiji.Core.Dll;
using Momiji.Core.Timer;
using Momiji.Interop.Opus;

namespace Momiji.Core.Opus;

[TestClass]
public class OpusExceptionUnitTest
{
    [TestMethod]
    public void Test1()
    {
        var test = new OpusException("test");
        Assert.IsNotNull(test.Message);
    }
}

[TestClass]
public class OpusUnitTest
{
    [TestMethod]
    public void Test1()
    {
        var configuration =
            new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("Momiji", LogLevel.Debug);
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
