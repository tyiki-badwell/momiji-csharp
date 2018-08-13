using Momiji.Core.H264;
using Momiji.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Test.H264File
{
    public class H264File : IDisposable
    {
        private bool disposed = false;

        private FileStream file;
        private BinaryReader reader;

        private Task processTask;

        public H264File()
        {
            var fileName = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + @"\sintel.h264";
            file = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            reader = new BinaryReader(file);
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
                            Trace.WriteLine($"[h264 file] Process Exception:{e.Message} {v.Message}");
                        }
                    }
                    processTask = null;
                }

                reader.Close();
                reader = null;
            }

            disposed = true;
        }

        public void Run(
            ISourceBlock<H264OutputBuffer> inputQueue,
            ITargetBlock<H264OutputBuffer> inputReleaseQueue,
            CancellationToken ct)
        {
            processTask = Process(inputQueue, inputReleaseQueue, ct);
        }

        private async Task Process(
            ISourceBlock<H264OutputBuffer> inputQueue,
            ITargetBlock<H264OutputBuffer> inputReleaseQueue,
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
                        bool got_sc = H264_get_nalu(data.Target, out int len);

                        if (got_sc)
                        {

                        }

                        data.Wrote = len;
                        data.EndOfFrame = true;

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

        private bool H264_get_nalu(byte[] buf, out int len)
        {
            len = 0;
            try
            {
                ulong sc = 0;

                while (true)
                {
                    var b = reader.ReadByte();
                    buf[len++] = b;

                    sc = (sc << 8) | b;

                    if (sc == 1 || ((sc & 0xFFFFFF) == 1))
                    {
                        len -= 3;
                        if (sc == 1)
                        {
                            len--;
                        }
                        return true;
                    }
                }

            }
            catch (EndOfStreamException ee)
            {
            }
            return false;
        }

        
    }
}