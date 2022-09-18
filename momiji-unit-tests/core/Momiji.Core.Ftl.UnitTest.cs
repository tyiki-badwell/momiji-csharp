using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Momiji.Core.Dll;
using Momiji.Core.Timer;

namespace Momiji.Core.Ftl;

[TestClass]
public class FtlExceptionUnitTest
{
    [TestMethod]
    public void Test1()
    {
        var test = new FtlException("test");
        Assert.IsNotNull(test.Message);
    }
}

[TestClass]
public class FtlUnitTest
{
    [TestMethod]
    public void Test1()
    {
        var configuration =
            new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();
        var streamKey = configuration["MIXER_STREAM_KEY"];
        var ingestHostname = configuration["MIXER_INGEST_HOSTNAME"];
        var mixerApiClientId = configuration["MIXER_USER_NAME"];

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

        //using var ftl = new FtlIngest(streamKey, ingestHostname, loggerFactory, timer, 1000, 1000, true, mixerApiClientId);
        //ftl.Connect();

    }
}
