using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Momiji.Core.Buffer;
using Momiji.Core.Timer;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.Wave;

[TestClass]
public class WaveExceptionUnitTest
{
    [TestMethod]
    public void Test1()
    {
        var test = new WaveException();
        Assert.IsNotNull(test.Message);
    }
}

[TestClass]
public class WaveOutShortUnitTest
{
    [TestMethod]
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
            builder.AddFilter("Momiji", LogLevel.Debug);
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
