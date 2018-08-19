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

                        var idx = 0;

                        var forbidden_zero_bit = (current[idx] & 0b10000000) >> 7;
                        var nal_ref_idc        = (current[idx] & 0b01100000) >> 5;
                        var nal_unit_type      = (current[idx] & 0b00011111);
                        idx++;
                        if (nal_unit_type == 14 || nal_unit_type == 20)
                        {
                            var svc_extension_flag = (current[idx] & 0b10000000) >> 7;
                            var idr_flag           = (current[idx] & 0b01000000) >> 6;
                            var priority_id        = (current[idx] & 0b00111111);
                            idx++;

                            var no_inter_layer_pred_flag = (current[idx] & 0b10000000) >> 7;
                            var dependency_id            = (current[idx] & 0b01110000) >> 4;
                            var quality_id               = (current[idx] & 0b00001111);
                            idx++;

                            var temporal_id           = (current[idx] & 0b11100000) >> 5;
                            var use_ref_base_pic_flag = (current[idx] & 0b00010000) >> 4;
                            var discardable_flag      = (current[idx] & 0b00001000) >> 3;
                            var output_flag           = (current[idx] & 0b00000100) >> 2;
                            var reserved_three_2bits  = (current[idx] & 0b00000011);
                            idx++;
                        }

                        Marshal.Copy(current, 0, data.AddrOfPinnedObject(), currentLen);
                        data.Wrote = currentLen;
                        data.EndOfFrame = true;

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