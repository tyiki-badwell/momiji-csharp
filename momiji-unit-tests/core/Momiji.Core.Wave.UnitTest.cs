using Microsoft.Extensions.Logging;
using Momiji.Core.Wave;
using Momiji.Interop.Wave;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace Momiji.Core
{
    public class WaveExceptionUnitTest
    {
        [Fact]
        public void Test1()
        {
            var test = new WaveException(MMRESULT.NOERROR);
            Assert.NotNull(test.Message);
        }
    }

    public class WaveOutShortUnitTest
    {
        [Fact]
        public void Test1()
        {
            uint deviceID = 0;
            ushort channels = 1;
            uint samplingRate = 4800;
            uint sampleLength = 10;
            var blockSize = (int)(samplingRate * sampleLength);
            WaveFormatExtensiblePart.SPEAKER channelMask = WaveFormatExtensiblePart.SPEAKER.ALL;
            using var loggerFactory = LoggerFactory.Create(builder => {
                builder.AddFilter("Momiji", LogLevel.Debug);
                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
                builder.AddConsole();
                builder.AddDebug();
            });
            using var timer = new Timer();

            using var pcmPool = new BufferPool<PcmBuffer<short>>(1, () => new PcmBuffer<short>(blockSize, 1), loggerFactory);
            {
                using var test = new WaveOutShort(deviceID, channels, samplingRate, channelMask, loggerFactory, timer, pcmPool);
                test.Execute(pcmPool.Receive(), default);
            }
        }
    }
}
