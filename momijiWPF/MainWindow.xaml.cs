using Microsoft.Extensions.Hosting;
using Momiji.Core.Vst.Worker;
using MomijiRT.Core.Vst;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Windows.Media.Devices;
using Windows.Media.Effects;
using Windows.Media.MediaProperties;
using WinRT;

namespace MomijiWPF
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private readonly List<IHost> list = new();

        private async void Button_Click_Run(object sender, RoutedEventArgs e)
        {
            var host = VstBridgeWorker.CreateHostBuilder(null).Build();
            list.Add(host);

            await host.StartAsync().ConfigureAwait(false);
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            await StopAsync().ConfigureAwait(false);
        }

        private void Button_Click_Open(object sender, RoutedEventArgs e)
        {
            foreach (var host in list)
            {
                var worker = (IRunner)host.Services.GetService(typeof(IRunner));
                worker.OpenEditor();
            }
        }

        private async void Button_Click_Close(object sender, RoutedEventArgs e)
        {
            var taskSet = new HashSet<Task>();
            foreach (var host in list)
            {
                var worker = (IRunner)host.Services.GetService(typeof(IRunner));
                taskSet.Add(worker.CloseEditorAsync());
            }
            await Task.WhenAll(taskSet).ConfigureAwait(false);
        }

        private async void Button_Click_Stop(object sender, RoutedEventArgs e)
        {
            await StopAsync().ConfigureAwait(false);
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

        AudioGraph audioGraph = null;
        private double audioWaveTheta = 0;
        private async void Button_Click_AppWindow(object sender, RoutedEventArgs e)
        {
            /*
            var v = CoreApplication.CreateNewView();
            await v.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => {
                var w = await AppWindow.TryCreateAsync();
                w.Closed += (sender, args) =>
                {
                    w = default;
                };

                await w.TryShowAsync();
            });
            */

            var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
            for(var idx = 0; idx < devices.Count; idx++)
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
                    QuantumSizeSelectionMode = QuantumSizeSelectionMode.LowestLatency,
                    DesiredSamplesPerQuantum = default,
                    //QuantumSizeSelectionMode = QuantumSizeSelectionMode.ClosestToDesired,
                    //DesiredSamplesPerQuantum = 10000,
                    AudioRenderCategory = Windows.Media.Render.AudioRenderCategory.GameMedia,
                    MaxPlaybackSpeedFactor = 1,
                    DesiredRenderDeviceAudioProcessing = Windows.Media.AudioProcessing.Raw,
                    EncodingProperties = default,
                    PrimaryRenderDevice = default
                };

                var result = await AudioGraph.CreateAsync(settings);
                if (result.Status != AudioGraphCreationStatus.Success)
                {
                    throw new Exception("create failed.", result.ExtendedError);
                }
                audioGraph = result.Graph;
            }
            Debug.Print($"audioGraph.SamplesPerQuantum {audioGraph.SamplesPerQuantum}");
            Debug.Print($"audioGraph.LatencyInSamples {audioGraph.LatencyInSamples}");

            AudioDeviceOutputNode outNode = null;
            {
                var result = await audioGraph.CreateDeviceOutputNodeAsync();
                if (result.Status != AudioDeviceNodeCreationStatus.Success)
                {
                    throw new Exception("create failed.", result.ExtendedError);
                }
                outNode = result.DeviceOutputNode;
                outNode.Start();
            }

            AudioFrameInputNode inNode = null;
            {
                var prop = AudioEncodingProperties.CreatePcm((uint)48000, 2, sizeof(float) * 8);
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
                    //Debug.Print($"args.RequiredSamples {args.RequiredSamples}");
                    if (args.RequiredSamples <= 0)
                    {
                        return;
                    }

                    var samples = (uint)args.RequiredSamples;
                    var bufferSize = samples * sizeof(float);

                    //TODO 別でメモリ管理する
                    var frame = new Windows.Media.AudioFrame(bufferSize);

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

            {
                var subNode = audioGraph.CreateSubmixNode();
                subNode.Stop();
                /*
                EchoEffectDefinition echoEffect = new EchoEffectDefinition(audioGraph);
                echoEffect.Delay = 1000.0;
                echoEffect.Feedback = .2;
                echoEffect.WetDryMix = .5;
                */
                //var ef = new Effect();


                //WinRT.ActivationFactory<EchoEffectDefinition>.As(GuidGenerator.GetIID(typeof(IEchoEffectDefinitionFactory).GetHelperType()));

                //var b = WinRT.Module.GetActivationFactory(typeof(Effect).FullName);
                //var a = ABI.MomijiRT.Core.Vst.Effect.FromAbi(b);

                //var a = new AudioEffectDefinition(typeof(EchoEffectDefinition).FullName);

                var a = new AudioEffectDefinition(typeof(Effect).FullName);

                //inNode.EffectDefinitions.Add(new Effect());
                subNode.EffectDefinitions.Add(a);
                
                //IAudioEffectDefinition
                //IBasicAudioEffect

                //inNode.AddOutgoingConnection(outNode);
                inNode.AddOutgoingConnection(subNode, 1.0);
                
                subNode.AddOutgoingConnection(outNode, 1.0);

                subNode.EnableEffectsByDefinition(a);

                subNode.Start();
            }

            audioGraph.Start();


        }
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
    /*
    public sealed class Effect : IBasicAudioEffect, IAudioEffectDefinition
    {
        public IReadOnlyList<AudioEncodingProperties> SupportedEncodingProperties
        {
            get
            {
                Debug.Print("SupportedEncodingProperties get");

                var supportedEncodingProperties = new List<AudioEncodingProperties>();
                AudioEncodingProperties encodingProps2 = AudioEncodingProperties.CreatePcm(48000, 2, 32);
                encodingProps2.Subtype = MediaEncodingSubtypes.Float;

                supportedEncodingProperties.Add(encodingProps2);

                return supportedEncodingProperties;
            }
        }

        public bool UseInputFrameForOutput
        {
            get
            {
                Debug.Print("UseInputFrameForOutput get");
                return false;
            }
        }

        public string ActivatableClassId
        {
            get
            {
                Debug.Print("ActivatableClassId get");
                return typeof(Effect).FullName;
            }
        }

        public IPropertySet Properties
        {
            get
            {
                Debug.Print("Properties get");
                return configuration;
            }
        }

        public void Close(MediaEffectClosedReason reason)
        {
            Debug.Print($"Close reason {reason}");
        }

        public void DiscardQueuedFrames()
        {
            Debug.Print("DiscardQueuedFrames");
        }

        private double audioWaveTheta = 0;
        public void ProcessFrame(ProcessAudioFrameContext context)
        {
            Debug.Print($"ProcessFrame context.InputFrame.RelativeTime {context.InputFrame.RelativeTime}");
            Debug.Print($"ProcessFrame context.InputFrame.SystemRelativeTime {context.InputFrame.SystemRelativeTime}");
            Debug.Print($"ProcessFrame context.InputFrame.Duration {context.InputFrame.Duration}");
            Debug.Print($"ProcessFrame context.InputFrame.Type {context.InputFrame.Type}");
            Debug.Print($"ProcessFrame context.InputFrame.IsDiscontinuous {context.InputFrame.IsDiscontinuous}");
            Debug.Print($"ProcessFrame context.InputFrame.IsReadOnly {context.InputFrame.IsReadOnly}");

            Debug.Print($"ProcessFrame context.OutputFrame.RelativeTime {context.OutputFrame.RelativeTime}");
            Debug.Print($"ProcessFrame context.OutputFrame.SystemRelativeTime {context.OutputFrame.SystemRelativeTime}");
            Debug.Print($"ProcessFrame context.OutputFrame.Duration {context.OutputFrame.Duration}");
            Debug.Print($"ProcessFrame context.OutputFrame.Type {context.OutputFrame.Type}");
            Debug.Print($"ProcessFrame context.OutputFrame.IsDiscontinuous {context.OutputFrame.IsDiscontinuous}");
            Debug.Print($"ProcessFrame context.OutputFrame.IsReadOnly {context.OutputFrame.IsReadOnly}");


            using (var inBuffer = context.InputFrame.LockBuffer(Windows.Media.AudioBufferAccessMode.Read))
            using (var outBuffer = context.OutputFrame.LockBuffer(Windows.Media.AudioBufferAccessMode.Write))
            using (var inReference = inBuffer.CreateReference())
            using (var outReference = outBuffer.CreateReference())
            {
                var samples = (uint)inBuffer.Length / sizeof(float);

                unsafe
                {
                    IMemoryBufferByteAccess outAccess = outReference.As<IMemoryBufferByteAccess>();
                    outAccess.GetBuffer(out byte* dataInBytes, out uint capacityInBytes);
                    var dataInFloat = (float*)dataInBytes;

                    float freq = 0.480f; // choosing to generate frequency of 1kHz
                    float amplitude = 0.3f;
                    int sampleRate = (int)encodingProperties.SampleRate;
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
        }

        private AudioEncodingProperties encodingProperties;

        public void SetEncodingProperties(AudioEncodingProperties encodingProperties)
        {
            this.encodingProperties = encodingProperties;

            Debug.Print($"encodingProperties.SampleRate {encodingProperties.SampleRate}");
            Debug.Print($"encodingProperties.Type {encodingProperties.Type}");
            Debug.Print($"encodingProperties.Subtype {encodingProperties.Subtype}");
            Debug.Print($"encodingProperties.ChannelCount {encodingProperties.ChannelCount}");
            Debug.Print($"encodingProperties.Bitrate {encodingProperties.Bitrate}");
            Debug.Print($"encodingProperties.BitsPerSample {encodingProperties.BitsPerSample}");
            Debug.Print($"encodingProperties.IsSpatial {encodingProperties.IsSpatial}");

            if (encodingProperties.Properties != default)
            {
                foreach (var key in encodingProperties.Properties.Keys)
                {
                    if (encodingProperties.Properties.TryGetValue(key, out var value))
                    {
                        Debug.Print($"encodingProperties.Properties {key} {value}");
                    }
                }
            }
        }

        private IPropertySet configuration;
        public void SetProperties(IPropertySet configuration)
        {
            this.configuration = configuration;

            foreach (var key in configuration.Keys)
            {
                if (configuration.TryGetValue(key, out var value))
                {
                    Debug.Print($"configuration {key} {value}");
                }
            }
        }
    }
    */
}

