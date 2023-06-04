using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Core.Timer;
using Xunit;

namespace Momiji.Core.Wave;

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
        var deviceID = 0;
        var channels = (short)1;
        var samplingRate = 4800;
        var sampleLength = 10;
        var blockSize = (samplingRate * sampleLength);
        var channelMask = SPEAKER.FrontLeft | SPEAKER.FrontRight;
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("Momiji", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
            builder.AddConsole();
            builder.AddDebug();
        });
        var counter = new ElapsedTimeCounter();

        using var pcmPool = new BufferPool<PcmBuffer<short>>(1, () => new PcmBuffer<short>(blockSize, 1), loggerFactory);
        {
            try
            {
                using var test = new WaveOutShort(deviceID, channels, samplingRate, channelMask, loggerFactory, counter, pcmPool);
                test.Execute(pcmPool.Receive(), default);
            }
            catch (WaveException)
            {

            }
        }
    }
}
