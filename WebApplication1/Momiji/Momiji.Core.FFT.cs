using Microsoft.Extensions.Logging;
using Momiji.Core.H264;
using Momiji.Core.Wave;
using Momiji.Core.WebMidi;
using Momiji.Interop;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.FFT
{
    public class FFTEncoder : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private int PicWidth { get; }
        private int PicHeight { get; }
        private float MaxFrameRate { get; }

        private bool disposed = false;

        private Bitmap bitmap;

        public FFTEncoder(
            int picWidth,
            int picHeight,
            float maxFrameRate,
            ILoggerFactory loggerFactory,
            Timer timer
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<FFTEncoder>();
            Timer = timer;

            PicWidth = picWidth;
            PicHeight = picHeight;
            MaxFrameRate = maxFrameRate;


            bitmap = new Bitmap(PicWidth, PicHeight, PixelFormat.Format24bppRgb);
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
                if (bitmap != null)
                {
                    bitmap.Dispose();
                    bitmap = null;
                }
            }

            disposed = true;
        }

        private List<string> list = new List<string>();
        private Dictionary<byte, byte> note = new Dictionary<byte, byte>();

        public void Receive(
            MIDIMessageEvent midiEvent
        )
        {
            list.Insert(0,
                $"{DateTimeOffset.FromUnixTimeMilliseconds((long)midiEvent.receivedTime).ToUniversalTime():HH:mm:ss.fff} => " +
                $"{DateTimeOffset.FromUnixTimeMilliseconds(Timer.USec / 1000).ToUniversalTime():HH:mm:ss.fff} " +
                ((midiEvent.data.Length >= 1) ? $"{midiEvent.data[0]:X2}" : "") +
                ((midiEvent.data.Length >= 2) ? $"{midiEvent.data[1]:X2}" : "") +
                ((midiEvent.data.Length >= 3) ? $"{midiEvent.data[2]:X2}" : "") +
                ((midiEvent.data.Length >= 4) ? $"{midiEvent.data[3]:X2}" : "")
            );
            if (list.Count > 20)
            {
                list.RemoveAt(20);
            }

            var m = midiEvent.data[0] & 0xF0;
            var k = midiEvent.data[1];
            var v = midiEvent.data[2];

            if (m == 0x80 || (m == 0x90))
            {
                note.Remove(k);
                if (m == 0x90 && v > 0)
                {
                    note.Add(k, v);
                }
            }
        }


        public void Execute(
            PcmBuffer<float> source,
            H264InputBuffer dest
        )
        {
            dest.Log.Marge(source.Log);
            dest.Log.Add("[fft] start", Timer.USecDouble);

            using (var g = Graphics.FromImage(bitmap))
            using (var fontFamily = new FontFamily(GenericFontFamilies.Monospace))
            using (var font = new Font(fontFamily, 15.0f))
            using (var black = new SolidBrush(Color.Black))
            using (var white = new SolidBrush(Color.White))
            {
                g.FillRectangle(black, 0, 0, PicWidth, PicHeight);
                foreach (var (k, v) in new Dictionary<byte, byte>(note)) //排他を掛けてないのでコピー
                {
                    using (var pen = new SolidBrush(Color.FromArgb(v, v, 0)))
                    {
                        g.FillRectangle(pen, 0, k * 2, PicWidth, 2);
                    }
                }

                var idx = 0;
                foreach (var s in new List<string>(list)) //排他を掛けてないのでコピー
                {
                    g.DrawString(s, font, white, 0, (idx++ * 15));
                }

                g.DrawString($"{DateTimeOffset.FromUnixTimeMilliseconds(Timer.USec / 1000).ToUniversalTime():HH:mm:ss.fff}", font, white, PicWidth - 200, PicHeight - 20);
            }
            dest.Log.Add("[fft] drawn", Timer.USecDouble);

            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, PicWidth, PicHeight), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                unsafe
                {
                    var target = dest.Target;
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
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
            dest.Log.Add("[fft] end", Timer.USecDouble);
        }
    }
}