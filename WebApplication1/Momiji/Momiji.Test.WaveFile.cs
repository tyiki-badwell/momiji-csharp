﻿using Microsoft.Extensions.Logging;
using Momiji.Interop;
using Momiji.Interop.Wave;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Test.WaveFile
{
    public class WaveFile : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        private bool disposed = false;

        private FileStream file;
        private BinaryWriter writer;

        private long riffSizePosition;
        private long dataSizePosition;
        private uint size;
        
        private Task processTask;

        public WaveFile(
            uint deviceID,
            ushort channels,
            uint samplesPerSecond,
            ushort bitsPerSample,
            WaveFormatExtensiblePart.SPEAKER channelMask,
            Guid formatSubType,
            uint samplesPerBuffer,
            ILoggerFactory loggerFactory
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<WaveFile>();

            var fileName = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + @"\test.wav";
            file = new FileStream(fileName, FileMode.Create, FileAccess.Write);
            writer = new BinaryWriter(file);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));

            riffSizePosition = dataSizePosition = file.Position;

            writer.Write((uint)0);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16U);
            writer.Write((ushort)WaveFormatEx.FORMAT.IEEE_FLOAT);
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
                if (processTask != null)
                {
                    try
                    {
                        processTask.Wait();
                    }
                    catch (AggregateException e)
                    {
                        foreach (var v in e.InnerExceptions)
                        {
                            Logger.LogInformation($"[wave file] Process Exception:{e.Message} {v.Message}");
                        }
                    }
                    processTask = null;
                }

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
                    var data = inputQueue.Receive(ct);
                    size += (uint)(data.Target.Length * Marshal.SizeOf<float>());
                    for (var idx = 0; idx < data.Target.Length; idx++)
                    {
                        writer.Write(data.Target[idx]);
                    }
                    inputReleaseQueue.Post(data);
                }
                Logger.LogInformation("[wave] loop end");
            });
        }
    }
}