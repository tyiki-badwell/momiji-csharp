using Momiji.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Dataflow;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Text;

namespace Momiji.Test.WaveFile
{
    public class WaveFile : IDisposable
    {
        private bool disposed = false;

        private FileStream file;
        private BinaryWriter writer;

        private long riffSizePosition;
        private long dataSizePosition;
        private uint size;
        
        private Task processTask;

        public WaveFile(
            UInt32 deviceID,
            UInt16 channels,
            UInt32 samplesPerSecond,
            UInt16 bitsPerSample,
            Wave.WaveFormatExtensiblePart.SPEAKER channelMask,
            Guid formatSubType,
            UInt32 samplesPerBuffer
        )
        {
            var fileName = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + @"\test.wav";
            file = new FileStream(fileName, FileMode.Create, FileAccess.Write);
            writer = new BinaryWriter(file);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));

            riffSizePosition = dataSizePosition = file.Position;

            writer.Write((uint)0);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16U);
            writer.Write((ushort)Wave.WaveFormatEx.FORMAT.IEEE_FLOAT);
            writer.Write(channels);
            writer.Write(samplesPerSecond);
            writer.Write((uint)(channels * bitsPerSample / 8 * samplesPerSecond));
            writer.Write((ushort)(channels * bitsPerSample / 8));
            writer.Write(bitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));

            dataSizePosition = file.Position;

            writer.Write((uint)0);
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
                writer.Flush();

                var fileSize = file.Position;
/*
                file.Seek(riffSizePosition, SeekOrigin.Begin);
                writer.Write(fileSize - 8);
                writer.Flush();

                file.Seek(dataSizePosition, SeekOrigin.Begin);
                writer.Write(size);
                writer.Flush();
                */
                writer.Dispose();
            }

            disposed = true;
        }


        public void Run(
            ISourceBlock<PinnedBuffer<float[]>> inputQueue,
            ITargetBlock<PinnedBuffer<float[]>> inputReleaseQueue,
            CancellationToken ct)
        {
            processTask = Process(inputQueue, inputReleaseQueue, ct);
        }

        private async Task Process(
            ISourceBlock<PinnedBuffer<float[]>> inputQueue,
            ITargetBlock<PinnedBuffer<float[]>> inputReleaseQueue,
            CancellationToken ct)
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        var data = inputQueue.Receive(new TimeSpan(20_000_000), ct);
                        size += (uint)(data.Target.Length * Marshal.SizeOf<float>());
                        for (var idx = 0; idx < data.Target.Length; idx++)
                        {
                            writer.Write(data.Target[idx]);
                        }
                        inputReleaseQueue.Post(data);
                    }
                    catch (TimeoutException te)
                    {
                        Trace.WriteLine("[wave] timeout");
                        continue;
                    }
                }
                Trace.WriteLine("[wave] loop end");
            });
        }
    }
}