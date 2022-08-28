using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation.Collections;
using Windows.Media.Effects;
using Windows.Media.MediaProperties;
using WinRT;

namespace MomijiRTEffect.Core.Vst
{
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    public sealed class Effect : IBasicAudioEffect
    {
        private AudioEncodingProperties? currentEncodingProperties;
        private List<AudioEncodingProperties> supportedEncodingProperties = new();

        public Effect()
        {
            Debug.Print("Effect create");

            var props = AudioEncodingProperties.CreatePcm(48000, 1, 32);
            props.Subtype = MediaEncodingSubtypes.Float;

            supportedEncodingProperties.Add(props);
        }
        ~Effect()
        {
            Debug.Print("Effect dispose");
        }

        public IReadOnlyList<AudioEncodingProperties> SupportedEncodingProperties
        {
            get
            {
                Debug.Print("SupportedEncodingProperties get");

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
            /*
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
            */

            using var inBuffer = context.InputFrame.LockBuffer(Windows.Media.AudioBufferAccessMode.Read);
            using var outBuffer = context.OutputFrame.LockBuffer(Windows.Media.AudioBufferAccessMode.Write);
            using var inReference = inBuffer.CreateReference();
            using var outReference = outBuffer.CreateReference();
            var samples = (uint)inBuffer.Length / sizeof(float);

            unsafe
            {
                IMemoryBufferByteAccess outAccess = ((IWinRTObject)outReference).As<IMemoryBufferByteAccess>();
                outAccess.GetBuffer(out byte* dataInBytes, out uint capacityInBytes);
                var dataInFloat = (float*)dataInBytes;

                float freq = 0.480f; // choosing to generate frequency of 1kHz
                float amplitude = 0.3f;
                int sampleRate = (currentEncodingProperties != default) ? (int)currentEncodingProperties.SampleRate: 48000;
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

        public void SetEncodingProperties(AudioEncodingProperties encodingProperties)
        {
            currentEncodingProperties = encodingProperties;

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

        private IPropertySet? configuration;
        public void SetProperties(IPropertySet configuration)
        {
            this.configuration = configuration;

            if (this.configuration == default)
            {
                Debug.Print("configuration EMPTY");
            }
            else
            {
                foreach (var key in configuration.Keys)
                {
                    if (configuration.TryGetValue(key, out var value))
                    {
                        Debug.Print($"configuration {key} {value}");
                    }
                }
            }
        }
    }
}
