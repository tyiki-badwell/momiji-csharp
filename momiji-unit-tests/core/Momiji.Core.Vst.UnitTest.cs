using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Dll;
using Momiji.Core.Timer;
using Momiji.Core.WebMidi;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace Momiji.Core.Vst
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

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddFilter("Momiji", LogLevel.Debug);
                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
                builder.AddConsole();
                builder.AddDebug();
            });
            var logger = loggerFactory.CreateLogger<VstUnitTest>();

            using var dllManager = new DllManager(configuration, loggerFactory);
            var counter = new ElapsedTimeCounter();

            var blockSize = 2880;

            var midiEventInput = new BufferBlock<MIDIMessageEvent2>();
            using var buffer = new VstBuffer<double>(blockSize, 2);

            using var vst = new AudioMaster<double>(48000, blockSize, loggerFactory, counter, dllManager);
            var effect = vst.AddEffect("Dexed.dll");
            var effect2 = vst.AddEffect("Synth1 VST.dll");

            //var aeffect = effect.GetAEffect();
            //for (int i = 0; i < 1/*aeffect.numParams*/; i++)
            //{
            //    var label = effect.GetParameterLabel(i);
            //    var name = effect.GetParameterName(i);
            //    var display = effect.GetParameterDisplay(i);
            //    var value = effect.GetParameter(i);
            //    logger.LogInformation($"VST Parameter {i}:{label}:{name}:{display}:{value}");
            //}

            {
                var nowTime = counter.NowTicks / 10;
                midiEventInput.Post(new MIDIMessageEvent2()
                {
                    midiMessageEvent = {
                    receivedTime = nowTime,
                    data0 = 0x90,
                    data1 = 0x20,
                    data2 = 0x40,
                    data3 = 0
                },
                    receivedTimeUSec = nowTime
                });
                midiEventInput.Post(new MIDIMessageEvent2()
                {
                    midiMessageEvent = {
                    receivedTime = nowTime,
                    data0 = 0x90,
                    data1 = 0x21,
                    data2 = 0x40,
                    data3 = 0
                },
                    receivedTimeUSec = nowTime
                });
                effect.ProcessEvent(nowTime, midiEventInput);
                effect.ProcessReplacing(nowTime, buffer);
            }

            {
                var nowTime = counter.NowTicks / 10;
                midiEventInput.Post(new MIDIMessageEvent2()
                {
                    midiMessageEvent = {
                    receivedTime = nowTime,
                    data0 = 0x90,
                    data1 = 0x30,
                    data2 = 0x40,
                    data3 = 0
                },
                    receivedTimeUSec = nowTime
                });
                effect.ProcessEvent(nowTime, midiEventInput);
                effect.ProcessReplacing(nowTime, buffer);
            }
            {
                var nowTime = counter.NowTicks / 10;
                midiEventInput.Post(new MIDIMessageEvent2()
                {
                    midiMessageEvent = {
                    receivedTime = nowTime,
                    data0 = 0x90,
                    data1 = 0x40,
                    data2 = 0x40,
                    data3 = 0
                },
                    receivedTimeUSec = nowTime
                });
                effect.ProcessEvent(nowTime, midiEventInput);
                effect.ProcessReplacing(nowTime, buffer);
            }
            {
                var nowTime = counter.NowTicks / 10;
                midiEventInput.Post(new MIDIMessageEvent2()
                {
                    midiMessageEvent = {
                    receivedTime = nowTime,
                    data0 = 0x90,
                    data1 = 0x50,
                    data2 = 0x40,
                    data3 = 0
                },
                    receivedTimeUSec = nowTime
                });
                effect.ProcessEvent(nowTime, midiEventInput);
                effect.ProcessReplacing(nowTime, buffer);
            }
        }

        [Fact]
        public void Test2()
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
            var logger = loggerFactory.CreateLogger<VstUnitTest>();

            using var dllManager = new DllManager(configuration, loggerFactory);
            var counter = new ElapsedTimeCounter();

            var blockSize = 2880;

            using var vst = new AudioMaster<float>(48000, blockSize, loggerFactory, counter, dllManager);
            using var processCancel = new CancellationTokenSource();

            var effect = vst.AddEffect("Synth1 VST.dll");
            effect.OpenEditor(processCancel.Token);
            processCancel.Cancel();
            effect.CloseEditorAsync().Wait();

        }

        [Fact]
        public void Test3()
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
            var logger = loggerFactory.CreateLogger<VstUnitTest>();

            using var dllManager = new DllManager(configuration, loggerFactory);
            var counter = new ElapsedTimeCounter();

            var blockSize = 2880;

            using var vst = new AudioMaster<float>(48000, blockSize, loggerFactory, counter, dllManager);

            var effect = vst.AddEffect("magical8bitPlug3.dll");

            vst.RemoveEffect(effect);
        }

    }
}
