using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Core.Timer;
using System;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace Momiji.Core.Wave
{
    public class WaveExceptionUnitTest
    {
        [Fact]
        public void Test1()
        {
            var test = new WaveException();
            Assert.NotNull(test.Message);
        }
    }

    public class WaveOutShortUnitTest
    {
        [Fact]
        public void Test1()
        {
            int deviceID = 0;
            short channels = 1;
            int samplingRate = 4800;
            int sampleLength = 10;
            var blockSize = (samplingRate * sampleLength);
            SPEAKER channelMask = SPEAKER.FrontLeft | SPEAKER.FrontRight;
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddFilter("Momiji", LogLevel.Debug);
                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
                builder.AddConsole();
                builder.AddDebug();
            });
            using var lapTimer = new LapTimer();

            using var pcmPool = new BufferPool<PcmBuffer<short>>(1, () => new PcmBuffer<short>(blockSize, 1), loggerFactory);
            {
                try
                {
                    using var test = new WaveOutShort(deviceID, channels, samplingRate, channelMask, loggerFactory, lapTimer, pcmPool);
                    test.Execute(pcmPool.Receive(), default);
                }
                catch (Exception)
                {

                }
            }
        }
    }
}
