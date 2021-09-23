using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Dll;
using Momiji.Core.Timer;
using Xunit;

namespace Momiji.Core.Ftl
{
    public class FtlExceptionUnitTest
    {
        [Fact]
        public void Test1()
        {
            var test = new FtlException("test");
            Assert.NotNull(test.Message);
        }
    }

    public class FtlUnitTest
    {
        [Fact]
        public void Test1()
        {
            var configuration =
                new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddUserSecrets<FtlUnitTest>()
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
            using var lapTimer = new LapTimer();
            using var dllManager = new DllManager(configuration, loggerFactory);

            //using var ftl = new FtlIngest(streamKey, ingestHostname, loggerFactory, timer, 1000, 1000, true, mixerApiClientId);
            //ftl.Connect();

        }
    }
}
