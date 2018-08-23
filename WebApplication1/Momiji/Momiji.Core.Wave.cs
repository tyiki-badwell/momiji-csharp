using Momiji.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Dataflow;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;

namespace Momiji.Core.Wave
{
    public class WaveException : Exception
    {
        public WaveException(Interop.Wave.MMRESULT mmResult) : base(makeMessage(mmResult))
        {
        }

        static private string makeMessage(Interop.Wave.MMRESULT mmResult)
        {
            var text = new System.Text.StringBuilder(256);
            Interop.Wave.waveOutGetErrorText(mmResult, text, (uint)text.Capacity);
            return text.ToString();
        }
    }

    public class PcmBuffer<T> : PinnedBuffer<T[]> where T : struct
    {
        public PcmBuffer(Int32 blockSize, int channels) : base(new T[blockSize * channels])
        {
        }
    }

    public class WaveOutShort : WaveOut<short>
    {
        public WaveOutShort(
            UInt32 deviceID,
            UInt16 channels,
            UInt32 samplesPerSecond,
            Interop.Wave.WaveFormatExtensiblePart.SPEAKER channelMask
        ) : base(
            deviceID,
            channels,
            samplesPerSecond,
            channelMask,
            new Guid("00000001-0000-0010-8000-00aa00389b71")
            )
        { }
    }

    public class WaveOutFloat : WaveOut<float>
    {
        public WaveOutFloat(
            UInt32 deviceID,
            UInt16 channels,
            UInt32 samplesPerSecond,
            Interop.Wave.WaveFormatExtensiblePart.SPEAKER channelMask
        ) : base(
            deviceID,
            channels,
            samplesPerSecond,
            channelMask,
            new Guid("00000003-0000-0010-8000-00aa00389b71")
            )
        { }
    }

    public class WaveOut<T> : IDisposable where T : struct
    {
        private bool disposed = false;

        private BufferPool<PinnedBuffer<Interop.Wave.WaveHeader>> headerPool = 
            new BufferPool<PinnedBuffer<Interop.Wave.WaveHeader>>(2, () => { return new PinnedBuffer<Interop.Wave.WaveHeader>(new Interop.Wave.WaveHeader()); });
        private BufferBlock<PinnedBuffer<Interop.Wave.WaveHeader>> headerQueue = null;
        private BufferBlock<IntPtr> releaseQueue = new BufferBlock<IntPtr>();

        private IDictionary<IntPtr, PinnedBuffer<Interop.Wave.WaveHeader>> headerBusyPool = new ConcurrentDictionary<IntPtr, PinnedBuffer<Interop.Wave.WaveHeader>>();
        private IDictionary<IntPtr, PcmBuffer<T>> dataBusyPool = new ConcurrentDictionary<IntPtr, PcmBuffer<T>>();

        private Interop.Wave.WaveOut handle;

        private int SIZE_OF_T { get; }
        private uint SIZE_OF_WAVEHEADER { get; }

        private void DriverCallBackProc(
            IntPtr hdrvr,
            Interop.Wave.DriverCallBack.MM_EXT_WINDOW_MESSAGE uMsg,
            IntPtr dwUser,
            IntPtr dw1,
            IntPtr dw2
        )
        {
            if (uMsg == Interop.Wave.DriverCallBack.MM_EXT_WINDOW_MESSAGE.WOM_DONE)
            {
                releaseQueue.Post(dw1);
            }
        }

        public WaveOut(
            UInt32 deviceID,
            UInt16 channels,
            UInt32 samplesPerSecond,
            Interop.Wave.WaveFormatExtensiblePart.SPEAKER channelMask,
            Guid formatSubType
        )
        {
            SIZE_OF_T = Marshal.SizeOf<T>();
            SIZE_OF_WAVEHEADER = (uint)Marshal.SizeOf<Interop.Wave.WaveHeader>();

            var format = new Interop.Wave.WaveFormatExtensible();
            format.wfe.formatType = Interop.Wave.WaveFormatEx.FORMAT.EXTENSIBLE;
            format.wfe.channels = channels;
            format.wfe.samplesPerSecond = samplesPerSecond;
            format.wfe.bitsPerSample = (ushort)(SIZE_OF_T * 8);
            format.wfe.blockAlign = (ushort)(format.wfe.channels * format.wfe.bitsPerSample / 8);
            format.wfe.averageBytesPerSecond = format.wfe.samplesPerSecond * format.wfe.blockAlign;
            format.wfe.size = (ushort)(Marshal.SizeOf<Interop.Wave.WaveFormatExtensiblePart>());

            format.exp.validBitsPerSample = format.wfe.bitsPerSample;
            format.exp.channelMask = channelMask;
            format.exp.subFormat = formatSubType;

            headerQueue = headerPool.makeBufferBlock();

            var mmResult =
                Interop.Wave.waveOutOpen(
                    out handle,
                    deviceID,
                    ref format,
                    DriverCallBackProc,
                    IntPtr.Zero,
                    (
                            Interop.Wave.DriverCallBack.TYPE.FUNCTION
                        | Interop.Wave.DriverCallBack.TYPE.WAVE_FORMAT_DIRECT
                    //	|	Interop.Wave.DriverCallBack.TYPE.WAVE_ALLOWSYNC
                    )
                );
            if (mmResult != Interop.Wave.MMRESULT.NOERROR)
            {
                throw new WaveException(mmResult);
            }
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
                Trace.WriteLine("[wave] stop");

                if (handle != null)
                {
                    if (
                        !handle.IsInvalid
                        && !handle.IsClosed
                    )
                    {
                        Reset();

                        //バッファの開放待ち
                        Trace.WriteLine($"[wave] wait busy buffers :{headerBusyPool.Count}");
                        while (headerBusyPool.Count > 0)
                        {
                            IntPtr headerPtr = IntPtr.Zero;
                            while(releaseQueue.TryReceive(out headerPtr))
                            {
                                Unprepare(headerPtr);
                            }

                            Thread.Sleep(1000);
                        }
                        Trace.WriteLine($"[wave] wait end :{headerBusyPool.Count}");

                        headerPool.Dispose();
                        headerPool = null;

                        handle.Close();
                    }
                }
            }

            disposed = true;
        }


        private IntPtr Prepare(PcmBuffer<T> data, CancellationToken ct)
        {
            var header = headerQueue.Receive(ct);
            {
                var waveHeader = header.Target;
                waveHeader.data = data.AddrOfPinnedObject();
                waveHeader.bufferLength = (uint)(data.Target.Length * SIZE_OF_T);
                waveHeader.flags = 0;
                waveHeader.loops = 1;

                waveHeader.bytesRecorded = 0;
                waveHeader.user = IntPtr.Zero;
                waveHeader.next = IntPtr.Zero;
                waveHeader.reserved = IntPtr.Zero;
            }

            var mmResult =
                Interop.Wave.waveOutPrepareHeader(
                    handle,
                    header.AddrOfPinnedObject(),
                    SIZE_OF_WAVEHEADER
                );
            if (mmResult != Interop.Wave.MMRESULT.NOERROR)
            {
                headerQueue.Post(header);
                throw new WaveException(mmResult);
            }
            headerBusyPool.Add(header.AddrOfPinnedObject(), header);
            dataBusyPool.Add(data.AddrOfPinnedObject(), data);
            return header.AddrOfPinnedObject();
        }

        private PcmBuffer<T> Unprepare(IntPtr headerPtr)
        {
            PinnedBuffer<Interop.Wave.WaveHeader> header;
            headerBusyPool.Remove(headerPtr, out header);

            var mmResult =
                Interop.Wave.waveOutUnprepareHeader(
                    handle,
                    headerPtr,
                    SIZE_OF_WAVEHEADER
                );
            if (mmResult != Interop.Wave.MMRESULT.NOERROR)
            {
                throw new WaveException(mmResult);
            }

            PcmBuffer<T> data;
            dataBusyPool.Remove(header.Target.data, out data);
            headerQueue.Post(header);

            return data;
        }

        static public UInt32 GetNumDevices()
        {
            return Interop.Wave.waveOutGetNumDevs();
        }

        static public Interop.Wave.WaveOutCapabilities GetCapabilities(
            System.UInt32 deviceID
        )
        {
            var caps = new Interop.Wave.WaveOutCapabilities();
            var mmResult =
                Interop.Wave.waveOutGetDevCaps(
                    deviceID,
                    ref caps,
                    (uint)Marshal.SizeOf<Interop.Wave.WaveOutCapabilities>()
                );
            if (mmResult != Interop.Wave.MMRESULT.NOERROR)
            {
                throw new WaveException(mmResult);
            }
            return caps;
        }

        public void Send(IntPtr headerPtr)
        {
            if (
                handle.IsInvalid
            || handle.IsClosed
            )
            {
                return;
            }

            var mmResult =
                Interop.Wave.waveOutWrite(
                    handle,
                    headerPtr,
                    SIZE_OF_WAVEHEADER
                );
            if (mmResult != Interop.Wave.MMRESULT.NOERROR)
            {
                Unprepare(headerPtr);
                throw new WaveException(mmResult);
            }
        }

        public void Reset()
        {
            if (
                handle.IsInvalid
            || handle.IsClosed
            )
            {
                return;
            }

            var mmResult = Interop.Wave.waveOutReset(handle);
            if (mmResult != Interop.Wave.MMRESULT.NOERROR)
            {
                throw new WaveException(mmResult);
            }
        }

        public async Task Run(
            ISourceBlock<PcmBuffer<T>> inputQueue,
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
                        var headerPtr = Prepare(data, ct);
                        Send(headerPtr);
                    }
                    catch (TimeoutException te)
                    {
                        Trace.WriteLine("[wave] process loop timeout");
                        continue;
                    }
                }
                Trace.WriteLine("[wave] process loop end");
            });
        }

        public async Task Release(
            ITargetBlock<PcmBuffer<T>> inputReleaseQueue,
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
                        var headerPtr = releaseQueue.Receive(new TimeSpan(20_000_000), ct);
                        var data = Unprepare(headerPtr);
                        inputReleaseQueue.Post(data);
                    }
                    catch (TimeoutException te)
                    {
                        Trace.WriteLine("[wave] release loop timeout");
                        continue;
                    }
                }
                Trace.WriteLine("[wave] release loop end");
            });
        }
    }
}