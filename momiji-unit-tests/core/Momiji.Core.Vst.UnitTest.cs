using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Vst;
using Momiji.Core.Wave;
using Momiji.Core.WebMidi;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace Momiji.Core
{
    public class VstExceptionUnitTest
    {
        [Fact]
        public void Test1()
        {
            var test = new VstException("test");
            Assert.NotNull(test.Message);
        }
    }

    public class VstUnitTest
    {
        [Fact]
        public void Test1()
        {
            var configuration = 
                new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            using var loggerFactory = LoggerFactory.Create(builder => {
                builder.AddFilter("Momiji", LogLevel.Debug);
                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
                builder.AddConsole();
                builder.AddDebug();
            });
            Dll.Setup(configuration, loggerFactory);

            using var timer = new Timer();

            var blockSize = 2880;

            using var pcmPool = new BufferPool<PcmBuffer<float>>(1, () => new PcmBuffer<float>(blockSize, 2), loggerFactory);
            var midiEventInput = new BufferBlock<MIDIMessageEvent2>();
            using var buffer = new VstBuffer<float>(blockSize, 2);

            using var vst = new AudioMaster<float>(48000, blockSize, loggerFactory, timer);
            var effect = vst.AddEffect("Synth1 VST.dll");

            effect.ProcessReplacing(buffer, pcmPool.ReceiveAsync(), midiEventInput);

        }

    }
}
