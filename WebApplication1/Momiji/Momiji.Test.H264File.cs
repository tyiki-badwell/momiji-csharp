using Momiji.Core.H264;
using Momiji.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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

                byte[] bufA = new byte[100000000];
                byte[] bufB = new byte[100000000];
                byte[] current = bufA;
                byte[] next = bufB;

                //棄てる
                H264_get_nalu(current, out int firstLen);

                H264_get_nalu(current, out int currentLen);
                H264_get_nalu(next, out int nextLen);

                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        var data = inputQueue.Receive(new TimeSpan(20_000_000), ct);

                        var forbidden_zero_bit = (current[0] & 0b10000000) >> 7;
                        var nal_ref_idc        = (current[0] & 0b01100000) >> 5;
                        var nal_unit_type      = (current[0] & 0b00011111);

                        Marshal.Copy(current, 0, data.AddrOfPinnedObject(), currentLen);
                        data.Wrote = currentLen;

                        Swap(ref current, ref next);
                        Swap(ref currentLen, ref nextLen);
                        if (!H264_get_nalu(next, out nextLen))
                        {
                            file.Seek(0, SeekOrigin.Begin);
                            //棄てる
                            H264_get_nalu(next, out firstLen);
                            H264_get_nalu(next, out nextLen);
                        }

                        inputReleaseQueue.Post(data);
                    }
                    catch (TimeoutException te)
                    {
                        Trace.WriteLine("[h264 file] timeout");
                        continue;
                    }
                }
                Trace.WriteLine("[h264 file] loop end");
            });
        }

        private void Swap<T>(ref T lhs, ref T rhs)
        {
            T temp;
            temp = lhs;
            lhs = rhs;
            rhs = temp;
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
                Trace.WriteLine("[h264 file] end of stream");
            }
            return false;
        }

        
    }
}