using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.RTWorkQueue;
using Momiji.Core.Timer;
using Windows.Devices.Enumeration;
using Xunit;

namespace Momiji.Core.WASAPI;

public class DeviceInformationUnitTest
{
    [Fact]
    public async Task Test1()
    {
        var configuration =
            new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("Momiji", LogLevel.Trace);
            builder.AddFilter("Momiji.Core.WorkQueue", LogLevel.Information);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
            builder.AddConsole();
            builder.AddDebug();
        });

        var logger = loggerFactory.CreateLogger<DeviceInformationUnitTest>();

        using var workQueuePlatformEventsHandler = new RTWorkQueuePlatformEventsHandler(loggerFactory);
        using var workQueueManager = new RTWorkQueueManager(configuration, loggerFactory);

        var c = await DeviceInformation.FindAllAsync(DeviceClass.AudioRender);
        foreach (var i in c)
        {
            logger.LogInformation($"""
                name:{i.Name}
                id:{i.Id}
                in lid:{i.EnclosureLocation.InLid} 
                in dock:{i.EnclosureLocation.InDock}
                panel:{i.EnclosureLocation.Panel}
                RotationAngleInDegreesClockwise:{i.EnclosureLocation.RotationAngleInDegreesClockwise}
                default:{i.IsDefault}
                enabled:{i.IsEnabled}
                kind:{i.Kind}
                ProtectionLevel:{i.Pairing.ProtectionLevel}
                CanPair:{i.Pairing.CanPair}
                Custom:{i.Pairing.Custom}
                IsPaired:{i.Pairing.IsPaired}
                """);

            foreach (var k in i.Properties.Keys)
            {
                var v = i.Properties[k];
                logger.LogInformation($"""
                key:{k}
                value:{v}
                """);
            }

            var counter = new ElapsedTimeCounter();

            using var wasapiOut = await WASAPIOut.ActivateAsync(workQueueManager, i.Id, loggerFactory);
            wasapiOut.Initialize(false, false, false);

            counter.Reset();

            var audioWaveTheta = 0.0;
            wasapiOut.Start((ptr, frames) => {
                logger.LogInformation($"process {ptr:X} {frames} {(double)counter.ElapsedTicks / 10000}");
                unsafe
                {
                    var data = new Span<float>((void*)ptr, frames * 2);

                    var freq = 0.480f; // choosing to generate frequency of 1kHz
                    var amplitude = 0.3f;
                    var sampleIncrement = (freq * (Math.PI * 2)) / 48000;
                    // Generate a 1kHz sine wave and populate the values in the memory buffer
                    var idx = 0;
                    for (var i = 0; i < frames; i++)
                    {
                        var sinValue = amplitude * Math.Sin(audioWaveTheta);
                        data[idx++] = (float)sinValue; //L
                        data[idx++] = (float)sinValue; //R
                        audioWaveTheta += sampleIncrement;
                    }
                }

                return frames;
            });

            await Task.Delay(1000);

            wasapiOut.Stop();

            await Task.Delay(500);
        }
    }


}
