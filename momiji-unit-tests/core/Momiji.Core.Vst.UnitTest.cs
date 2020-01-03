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
            var logger = loggerFactory.CreateLogger<VstUnitTest>();

            using var dllManager = new DllManager(configuration, loggerFactory);
            using var timer = new Timer();

            var blockSize = 2880;

            using var pcmPool = new BufferPool<PcmBuffer<float>>(5, () => new PcmBuffer<float>(blockSize, 2), loggerFactory);
            var midiEventInput = new BufferBlock<MIDIMessageEvent2>();
            using var buffer = new VstBuffer<float>(blockSize, 2);

            using var vst = new AudioMaster<float>(48000, blockSize, loggerFactory, timer, dllManager);
            var effect = vst.AddEffect("Synth1 VST.dll");
            
            var aeffect = effect.GetAEffect();
            for (int i = 0; i < 1 /*aeffect.numParams*/; i++)
            {
                var label = effect.GetParameterLabel(i);
                var name = effect.GetParameterName(i);
                var display = effect.GetParameterDisplay(i);
                var value = effect.GetParameter(i);
                logger.LogInformation($"{i}:{label}:{name}:{display}:{value}");
            }
            
            midiEventInput.Post(new MIDIMessageEvent2() {
                midiMessageEvent = {
                    receivedTime = 0,
                    data0 = 0x90,
                    data1 = 0x20,
                    data2 = 0x40,
                    data3 = 0
                },
                receivedTimeUSec = 0
            });
            effect.ProcessReplacing(buffer, pcmPool.ReceiveAsync(), midiEventInput);

            midiEventInput.Post(new MIDIMessageEvent2()
            {
                midiMessageEvent = {
                    receivedTime = 0,
                    data0 = 0x90,
                    data1 = 0x30,
                    data2 = 0x40,
                    data3 = 0
                },
                receivedTimeUSec = 0
            });
            effect.ProcessReplacing(buffer, pcmPool.ReceiveAsync(), midiEventInput);

            midiEventInput.Post(new MIDIMessageEvent2()
            {
                midiMessageEvent = {
                    receivedTime = 0,
                    data0 = 0x90,
                    data1 = 0x40,
                    data2 = 0x40,
                    data3 = 0
                },
                receivedTimeUSec = 0
            });
            effect.ProcessReplacing(buffer, pcmPool.ReceiveAsync(), midiEventInput);

            midiEventInput.Post(new MIDIMessageEvent2()
            {
                midiMessageEvent = {
                    receivedTime = 0,
                    data0 = 0x90,
                    data1 = 0x50,
                    data2 = 0x40,
                    data3 = 0
                },
                receivedTimeUSec = 0
            });
            effect.ProcessReplacing(buffer, pcmPool.ReceiveAsync(), midiEventInput);

        }

    }
}
