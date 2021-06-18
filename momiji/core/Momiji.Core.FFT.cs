using Microsoft.Extensions.Logging;
using Momiji.Core.H264;
using Momiji.Core.Timer;
using Momiji.Core.Wave;
using Momiji.Core.WebMidi;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Momiji.Core.FFT
{
    public class FFTEncoder : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private LapTimer LapTimer { get; }

        private int PicWidth { get; }
        private int PicHeight { get; }
        private float MaxFrameRate { get; }

        private bool disposed;

        private Bitmap bitmapInput;
        private Bitmap bitmapOutput;
        private Bitmap bitmapTemp;

        private readonly object syncInput = new();
        private readonly object syncOutput = new();

        public FFTEncoder(
            int picWidth,
            int picHeight,
            float maxFrameRate,
            ILoggerFactory loggerFactory,
            LapTimer lapTimer
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<FFTEncoder>();
            LapTimer = lapTimer;

            PicWidth = picWidth;
            PicHeight = picHeight;
            MaxFrameRate = maxFrameRate;

            bitmapInput = new Bitmap(PicWidth, PicHeight, PixelFormat.Format24bppRgb)
            {
                Tag = "A"
            };
            bitmapOutput = new Bitmap(bitmapInput)
            {
                Tag = "B"
            };
            bitmapTemp = new Bitmap(bitmapInput)
            {
                Tag = "C"
            };
        }

        ~FFTEncoder()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;

            if (disposing)
            {
            }

            bitmapInput?.Dispose();
            bitmapInput = null;

            bitmapOutput?.Dispose();
            bitmapOutput = null;

            bitmapTemp?.Dispose();
            bitmapTemp = null;

            disposed = true;
        }

        private readonly List<string> list = new();
        private readonly Dictionary<byte, byte> note = new();

        public void Receive(
            MIDIMessageEvent2 midiEvent
        )
        {
            list.Insert(0,
                $"{DateTimeOffset.FromUnixTimeMilliseconds((long)midiEvent.midiMessageEvent.receivedTime).ToUniversalTime():HH:mm:ss.fff} => " +
                $"{DateTimeOffset.FromUnixTimeMilliseconds((long)(midiEvent.receivedTimeUSec / 1000)).ToUniversalTime():HH:mm:ss.fff} => " +
                $"{DateTimeOffset.FromUnixTimeMilliseconds((long)(LapTimer.USecDouble / 1000)).ToUniversalTime():HH:mm:ss.fff} " +
                $"{midiEvent.midiMessageEvent.data0:X2}" +
                $"{midiEvent.midiMessageEvent.data1:X2}" +
                $"{midiEvent.midiMessageEvent.data2:X2}" +
                $"{midiEvent.midiMessageEvent.data3:X2}"
            );
            if (list.Count > 20)
            {
                list.RemoveAt(20);
            }

            var m = midiEvent.midiMessageEvent.data0 & 0xF0;
            var k = midiEvent.midiMessageEvent.data1;
            var v = midiEvent.midiMessageEvent.data2;

            if (m == 0x80 || (m == 0x90))
            {
                note.Remove(k);
                if (m == 0x90 && v > 0)
                {
                    note.Add(k, v);
                }
            }
        }

        public void Receive(
            PcmBuffer<float> source
        )
        {
            if (source == default)
            {
                throw new ArgumentNullException(nameof(source));
            }
            source.Log.Add("[fft] start", LapTimer.USecDouble);

            lock (syncInput)
            {
                using var g = Graphics.FromImage(bitmapInput);
                using var fontFamily = new FontFamily(GenericFontFamilies.Monospace);
                using var font = new Font(fontFamily, 15.0f);
                using var black = new SolidBrush(Color.Black);
                using var white = new SolidBrush(Color.White);

                g.FillRectangle(black, 0, 0, PicWidth, PicHeight);

                //TODO FFT
                var data = source.Buffer.Target.AsSpan();
                var max = 0.0;
                var min = 0.0;
                var center = PicHeight / 2;
                for (var i = 0; i < data.Length && i < PicWidth; i++)
                {
                    var f = data[i];
                    max = (max < f) ? f : max;
                    min = (min > f) ? f : min;

                    var v = (byte)((f * 100) + 100);
                    using var pen = new SolidBrush(Color.FromArgb(0, v, v));

                    var y = center;
                    var h = 0;
                    if (f < 0)
                    {
                        y += (int)(f * center);
                        h -= (int)(f * center);
                    }
                    else
                    {
                        h += (int)(f * center);
                    }

                    g.FillRectangle(pen, i, y, 1, h);
                }

                foreach (var (k, v) in new Dictionary<byte, byte>(note)) //排他を掛けてないのでコピー
                {
                    using var pen = new SolidBrush(Color.FromArgb(v, v, 0));
                    g.FillRectangle(pen, 0, k * 2, PicWidth, 2);
                }

                var idx = 0;
                foreach (var s in new List<string>(list)) //排他を掛けてないのでコピー
                {
                    g.DrawString(s, font, white, 0, (idx++ * 15));
                }

                g.DrawString($"{data.Length} {max} {min}", font, white, 0, PicHeight - 50);
                g.DrawString($"{DateTimeOffset.FromUnixTimeMilliseconds(LapTimer.USec / 1000).ToUniversalTime():HH:mm:ss.fff}", font, white, PicWidth - 200, PicHeight - 20);
            }
            source.Log.Add("[fft] drawn", LapTimer.USecDouble);
        }

        public void Execute(
            H264InputBuffer dest
        )
        {
            if (dest == default)
            {
                throw new ArgumentNullException(nameof(dest));
            }

            lock (syncOutput)
            {
                var bitmapData = bitmapOutput.LockBits(new Rectangle(0, 0, PicWidth, PicHeight), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    //dest.Log.Marge(source.Log);
                    unsafe
                    {
                        var target = dest.SSourcePictureBuffer.Target;
                        var length = target.iPicWidth * target.iPicHeight;

                        var s = new Span<byte>((byte*)bitmapData.Scan0, length * 3);
                        var y = new Span<byte>((byte*)target.pData0, length);
                        var u = new Span<byte>((byte*)target.pData1, length >> 2);
                        var v = new Span<byte>((byte*)target.pData2, length >> 2);

                        var (sPos, yPos, uPos, vPos) = (0, 0, 0, 0);

                        /*
                          Y = round( 0.256788 * R + 0.504129 * G + 0.097906 * B) +  16
                          U = round(-0.148223 * R - 0.290993 * G + 0.439216 * B) + 128
                          V = round( 0.439216 * R - 0.367788 * G - 0.071427 * B) + 128
                        */

                        var hOdd = true;
                        for (var h = 0; h < bitmapData.Height; h++)
                        {
                            var wOdd = true;
                            for (var w = 0; w < bitmapData.Width; w++)
                            {
                                var b = s[sPos++];
                                var g = s[sPos++];
                                var r = s[sPos++];

                                y[yPos++] = (byte)(((256788 * r + 504129 * g + 97906 * b) / 1000000) + 16);

                                if (hOdd && wOdd)
                                {
                                    u[uPos++] = (byte)(((-148223 * r - 290993 * g + 439216 * b) / 1000000) + 128);
                                    v[vPos++] = (byte)(((439216 * r - 367788 * g - 71427 * b) / 1000000) + 128);
                                }
                                wOdd = !wOdd;
                            }
                            hOdd = !hOdd;
                        }
                    }
                    dest.Log.Add("[fft] end", LapTimer.USecDouble);
                }
                finally
                {
                    bitmapOutput.UnlockBits(bitmapData);
                }

                //flip
                (bitmapInput, bitmapOutput, bitmapTemp) = (bitmapOutput, bitmapTemp, bitmapInput);
            }
        }
    }
}