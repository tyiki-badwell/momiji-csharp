using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momiji.Core.Timer;
using Momiji.Core.Vst.Worker;
using Momiji.Core.WebMidi;
using Momiji.Core.Window;
using MomijiRTEffect.Core.Vst;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Windows.Media.Devices;
using Windows.Media.Effects;
using Windows.Media.MediaProperties;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MomijiDriver;
/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private readonly List<IHost> list = new();

    private async Task RunAsync()
    {
        var host = VstBridgeWorker.CreateHost(Array.Empty<string>());
        list.Add(host);

        await host.StartAsync().ConfigureAwait(false);
    }

    private async Task StopAsync()
    {
        var taskSet = new HashSet<Task>();

        foreach (var host in list)
        {
            taskSet.Add(host.StopAsync());
        }
        await Task.WhenAll(taskSet).ConfigureAwait(false);
        foreach (var host in list)
        {
            host.Dispose();
        }
        list.Clear();
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {

    }

    private async void Window_Closed(object sender, WindowEventArgs args)
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync().ConfigureAwait(false);
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void OpenEditor_Click(object sender, RoutedEventArgs e)
    {
        list.AsParallel().ForAll(host =>
        {
            var worker = (IRunner?)host.Services.GetService(typeof(IRunner));
            if (worker == null)
            {
                return;
            }
            var window = worker.OpenEditor();

            window.SetWindowStyle(
                0x00800000 // WS_BORDER
                | 0x00C00000 //WS_CAPTION
                | 0x00010000 //WS_MAXIMIZEBOX
                | 0x00020000 //WS_MINIMIZEBOX
                | 0x00000000 //WS_OVERLAPPED
                | 0x00080000 //WS_SYSMENU
                | 0x00040000 //WS_THICKFRAME
            );

            window.Show(1);
        });
    }

    private void CloseEditor_Click(object sender, RoutedEventArgs e)
    {
        list.AsParallel().ForAll(host => {
            var worker = (IRunner?)host.Services.GetService(typeof(IRunner));
            if (worker == null)
            {
                return;
            }
            worker.CloseEditor();
        });
    }

    private void Window_Click(object sender, RoutedEventArgs e)
    {
        var window = AppWindow.Create();
        window.Show();
    }

    private void Window2_Click(object sender, RoutedEventArgs e)
    {
        list.AsParallel().ForAll(host => {
            var manager = (IWindowManager?)host.Services.GetService(typeof(IWindowManager));
            if (manager == null)
            {
                return;
            }
            var window = manager.CreateWindow();
            window.Move(0, 0, 200, 200, true);

            window.SetWindowStyle(
                0x00800000 // WS_BORDER
                | 0x00C00000 //WS_CAPTION
                | 0x00010000 //WS_MAXIMIZEBOX
                | 0x00020000 //WS_MINIMIZEBOX
                | 0x00000000 //WS_OVERLAPPED
                | 0x00080000 //WS_SYSMENU
                | 0x00040000 //WS_THICKFRAME
            );

            window.Show(1);
        });
    }

    private AudioGraph? audioGraph;
    private readonly ElapsedTimeCounter counter = new();
    private double before;

    private AudioEffectDefinition? effect;
    private async void Audio_Click(object sender, RoutedEventArgs e)
    {
        var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
        for (var idx = 0; idx < devices.Count; idx++)
        {
            var device = devices[idx];
            Debug.Print("-------------------------------------");
            Debug.Print($"device.Id {device.Id}");
            Debug.Print($"device.IsDefault {device.IsDefault}");
            Debug.Print($"device.IsEnabled {device.IsEnabled}");
            Debug.Print($"device.Kind {device.Kind}");
            Debug.Print($"device.Name {device.Name}");
            Debug.Print($"device.Pairing {device.Pairing}");
            /*
            try
            {
                if (device.Properties != default)
                {
                    foreach (var key in device.Properties.Keys)
                    {
                        if (device.Properties.TryGetValue(key, out var value))
                        {
                            Debug.Print($"device.Properties {key} {value}");
                        }
                    }
                }
            }
            catch(Exception _)
            {
            }
            */
        }
        Debug.Print("-------------------------------------");

        {
            var settings = new AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.Media)
            {
                QuantumSizeSelectionMode = QuantumSizeSelectionMode.SystemDefault,
                //QuantumSizeSelectionMode = QuantumSizeSelectionMode.LowestLatency,
                DesiredSamplesPerQuantum = default,
                //QuantumSizeSelectionMode = QuantumSizeSelectionMode.ClosestToDesired,
                //DesiredSamplesPerQuantum = 10000,
                AudioRenderCategory = Windows.Media.Render.AudioRenderCategory.GameMedia,
                MaxPlaybackSpeedFactor = 1,
                //DesiredRenderDeviceAudioProcessing = Windows.Media.AudioProcessing.Raw,
                DesiredRenderDeviceAudioProcessing = Windows.Media.AudioProcessing.Default,
                EncodingProperties = default,
                PrimaryRenderDevice = default
            };

            var result = await AudioGraph.CreateAsync(settings);
            if (result.Status != AudioGraphCreationStatus.Success)
            {
                throw new InvalidOperationException("create failed.", result.ExtendedError);
            }
            audioGraph = result.Graph;
        }
        Debug.Print($"audioGraph.SamplesPerQuantum {audioGraph.SamplesPerQuantum}");
        Debug.Print($"audioGraph.LatencyInSamples {audioGraph.LatencyInSamples}");

        AudioDeviceOutputNode outNode;
        {
            var result = await audioGraph.CreateDeviceOutputNodeAsync();
            if (result.Status != AudioDeviceNodeCreationStatus.Success)
            {
                throw new InvalidOperationException("create failed.", result.ExtendedError);
            }
            outNode = result.DeviceOutputNode;
            outNode.Start();
        }

        AudioFrameInputNode inNode;
        {
            var prop = AudioEncodingProperties.CreatePcm(48000, 2, sizeof(float) * 8);
            prop.Subtype = MediaEncodingSubtypes.Float;

            Debug.Print($"prop.SampleRate {prop.SampleRate}");
            Debug.Print($"prop.Type {prop.Type}");
            Debug.Print($"prop.Subtype {prop.Subtype}");
            Debug.Print($"prop.ChannelCount {prop.ChannelCount}");
            Debug.Print($"prop.Bitrate {prop.Bitrate}");
            Debug.Print($"prop.BitsPerSample {prop.BitsPerSample}");
            Debug.Print($"prop.IsSpatial {prop.IsSpatial}");

            if (prop.Properties != default)
            {
                foreach (var key in prop.Properties.Keys)
                {
                    if (prop.Properties.TryGetValue(key, out var value))
                    {
                        Debug.Print($"prop.Properties {key} {value}");
                    }
                }
            }

            inNode = audioGraph.CreateFrameInputNode(prop);
            inNode.Stop();
            inNode.QuantumStarted += (AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args) =>
            {
                var now = counter.NowTicks;
                //Debug.Print($"LAP {now - before}");
                before = now;

                //Debug.Print($"args.RequiredSamples {args.RequiredSamples}");
                if (args.RequiredSamples <= 0)
                {
                    return;
                }

                var samples = (uint)args.RequiredSamples;
                var bufferSize = samples * sizeof(float);

                //TODO •Ê‚Åƒƒ‚ƒŠŠÇ—‚·‚é
                var frame = new Windows.Media.AudioFrame(bufferSize);

                /*
                using (var buffer = frame.LockBuffer(Windows.Media.AudioBufferAccessMode.Write))
                using (var reference = buffer.CreateReference())
                {
                    unsafe
                    {
                        IMemoryBufferByteAccess a = ((IWinRTObject)reference).As<IMemoryBufferByteAccess>();
                        a.GetBuffer(out byte* dataInBytes, out uint capacityInBytes);
                        var dataInFloat = (float*)dataInBytes;
                        float freq = 0.480f; // choosing to generate frequency of 1kHz
                        float amplitude = 0.3f;
                        int sampleRate = (int)audioGraph.EncodingProperties.SampleRate;
                        double sampleIncrement = (freq * (Math.PI * 2)) / sampleRate;
                        // Generate a 1kHz sine wave and populate the values in the memory buffer
                        for (int i = 0; i < samples; i++)
                        {
                            double sinValue = amplitude * Math.Sin(audioWaveTheta);
                            dataInFloat[i] = (float)sinValue;
                            audioWaveTheta += sampleIncrement;
                        }
                    }
                }
                */

                sender.AddFrame(frame);
            };

            //inNode.AddOutgoingConnection(outNode);

            inNode.Start();
        }
        /*
        AudioFrameOutputNode frameOutNode;
        {
            frameOutNode = audioGraph.CreateFrameOutputNode();
            frameOutNode.Stop();
            audioGraph.QuantumStarted += (AudioGraph sender, object args) =>
            {
                Debug.Print($"args {args}");
                var frame = frameOutNode.GetFrame();
            };
            frameOutNode.Start();
        }
        */

        //if (false)
        {

            var subNode = audioGraph.CreateSubmixNode();
            subNode.Stop();

            //var _e = new Effect();

            //var _e = Activator.CreateInstance<Effect>();

            effect = new AudioEffectDefinition(typeof(Effect).FullName);

            subNode.EffectDefinitions.Add(effect);

            inNode.AddOutgoingConnection(subNode, 1.0);

            subNode.AddOutgoingConnection(outNode, 1.0);

            subNode.EnableEffectsByDefinition(effect);

            subNode.Start();

        }

        audioGraph.Start();

    }

    private void Note_Click(object sender, RoutedEventArgs e)
    {
        var b = (Button)sender;
        var note = (string)b.Tag;

        var m = new MIDIMessageEvent();
        m.receivedTime = 0;
        m.data0 = byte.Parse(note.Substring(0, 2), NumberStyles.HexNumber);
        m.data1 = byte.Parse(note.Substring(2, 2), NumberStyles.HexNumber);
        m.data2 = byte.Parse(note.Substring(4, 2), NumberStyles.HexNumber);
        m.data3 = 0;

        list.AsParallel().ForAll(host => {
            var worker = (IRunner?)host.Services.GetService(typeof(IRunner));
            if (worker == null)
            {
                return;
            }
            worker.Note(m);
        });
    }
}
