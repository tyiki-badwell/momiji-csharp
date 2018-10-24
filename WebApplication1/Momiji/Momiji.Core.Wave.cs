using Microsoft.Extensions.Logging;
using Momiji.Interop;
using Momiji.Interop.Wave;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.Wave
{
    public class WaveException : Exception
    {
        public WaveException(MMRESULT mmResult) : base(MakeMessage(mmResult))
        {
        }

        static private string MakeMessage(MMRESULT mmResult)
        {
            var text = new System.Text.StringBuilder(256);
            WaveOutMethod.waveOutGetErrorText(mmResult, text, (uint)text.Capacity);
            return text.ToString();
        }
    }

    public class PcmBuffer<T> : PinnedBuffer<T[]> where T : struct
    {
        public PcmBuffer(int blockSize, int channels) : base(new T[blockSize * channels])
        {
        }
    }

    public class WaveOutShort : WaveOut<short>
    {
        public WaveOutShort(
            uint deviceID,
            ushort channels,
            uint samplesPerSecond,
            WaveFormatExtensiblePart.SPEAKER channelMask,
            ILoggerFactory loggerFactory,
            Timer timer
        ) : base(
            deviceID,
            channels,
            samplesPerSecond,
            channelMask,
            new Guid("00000001-0000-0010-8000-00aa00389b71"),
            loggerFactory,
            timer
            )
        { }
    }

    public class WaveOutFloat : WaveOut<float>
    {
        public WaveOutFloat(
            uint deviceID,
            ushort channels,
            uint samplesPerSecond,
            WaveFormatExtensiblePart.SPEAKER channelMask,
            ILoggerFactory loggerFactory,
            Timer timer
        ) : base(
            deviceID,
            channels,
            samplesPerSecond,
            channelMask,
            new Guid("00000003-0000-0010-8000-00aa00389b71"),
            loggerFactory,
            timer
            )
        { }
    }

    public class WaveOut<T> : IDisposable where T : struct
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private bool disposed = false;

        private BufferPool<PinnedBuffer<WaveHeader>> headerPool;
        private BufferBlock<IntPtr> releaseQueue = new BufferBlock<IntPtr>();

        private IDictionary<IntPtr, PinnedBuffer<WaveHeader>> headerBusyPool = new ConcurrentDictionary<IntPtr, PinnedBuffer<WaveHeader>>();
        private IDictionary<IntPtr, PcmBuffer<T>> dataBusyPool = new ConcurrentDictionary<IntPtr, PcmBuffer<T>>();

        private PinnedDelegate<DriverCallBack.Delegate> driverCallBack;
        private WaveOut handle;

        private int SIZE_OF_T { get; }
        private uint SIZE_OF_WAVEHEADER { get; }

        private void DriverCallBackProc(
            IntPtr hdrvr,
            DriverCallBack.MM_EXT_WINDOW_MESSAGE uMsg,
            IntPtr dwUser,
            IntPtr dw1,
            IntPtr dw2
        )
        {
            if (uMsg == DriverCallBack.MM_EXT_WINDOW_MESSAGE.WOM_DONE)
            {
                releaseQueue.Post(dw1);
            }
        }

        public WaveOut(
            uint deviceID,
            ushort channels,
            uint samplesPerSecond,
            WaveFormatExtensiblePart.SPEAKER channelMask,
            Guid formatSubType, 
            ILoggerFactory loggerFactory,
            Timer timer
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<WaveOut<T>>();
            Timer = timer;

            SIZE_OF_T = Marshal.SizeOf<T>();
            SIZE_OF_WAVEHEADER = (uint)Marshal.SizeOf<WaveHeader>();

            var format = new WaveFormatExtensible();
            format.wfe.formatType = WaveFormatEx.FORMAT.EXTENSIBLE;
            format.wfe.channels = channels;
            format.wfe.samplesPerSecond = samplesPerSecond;
            format.wfe.bitsPerSample = (ushort)(SIZE_OF_T * 8);
            format.wfe.blockAlign = (ushort)(format.wfe.channels * format.wfe.bitsPerSample / 8);
            format.wfe.averageBytesPerSecond = format.wfe.samplesPerSecond * format.wfe.blockAlign;
            format.wfe.size = (ushort)(Marshal.SizeOf<WaveFormatExtensiblePart>());

            format.exp.validBitsPerSample = format.wfe.bitsPerSample;
            format.exp.channelMask = channelMask;
            format.exp.subFormat = formatSubType;

            headerPool = new BufferPool<PinnedBuffer<WaveHeader>>(2, () => { return new PinnedBuffer<WaveHeader>(new WaveHeader()); }, LoggerFactory);

            driverCallBack = new PinnedDelegate<DriverCallBack.Delegate>(new DriverCallBack.Delegate(DriverCallBackProc));

            var mmResult =
                WaveOut.waveOutOpen(
                    out handle,
                    deviceID,
                    ref format,
                    driverCallBack.FunctionPointer,
                    IntPtr.Zero,
                    (
                          DriverCallBack.TYPE.FUNCTION
                        | DriverCallBack.TYPE.WAVE_FORMAT_DIRECT
                    //	| DriverCallBack.TYPE.WAVE_ALLOWSYNC
                    )
                );
            if (mmResult != MMRESULT.NOERROR)
            {
                throw new WaveException(mmResult);
            }
        }

        ~WaveOut()
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

            if (handle != null)
            {
                if (
                    !handle.IsInvalid
                    && !handle.IsClosed
                )
                {
                    Logger.LogInformation("[wave] stop");
                    Reset();

                    //バッファの開放待ち
                    Logger.LogInformation($"[wave] wait busy buffers :{headerBusyPool.Count}");
                    while (headerBusyPool.Count > 0)
                    {
                        IntPtr headerPtr = IntPtr.Zero;
                        while (releaseQueue.TryReceive(out headerPtr))
                        {
                            Unprepare(headerPtr);
                        }

                        Thread.Sleep(1000);
                    }
                    Logger.LogInformation($"[wave] wait end :{headerBusyPool.Count}");

                    headerPool.Dispose();
                    headerPool = null;

                    handle.Close();
                }
                handle = null;
            }

            if (driverCallBack != null)
            {
                driverCallBack.Dispose();
                driverCallBack = null;
            }

            disposed = true;
        }


        private IntPtr Prepare(PcmBuffer<T> source, CancellationToken ct)
        {
            var header = headerPool.Receive(ct);
            {
                var waveHeader = header.Target;
                waveHeader.data = source.AddrOfPinnedObject;
                waveHeader.bufferLength = (uint)(source.Target.Length * SIZE_OF_T);
                waveHeader.flags = 0;
                waveHeader.loops = 1;

                waveHeader.bytesRecorded = 0;
                waveHeader.user = IntPtr.Zero;
                waveHeader.next = IntPtr.Zero;
                waveHeader.reserved = IntPtr.Zero;
            }

            var mmResult =
                handle.waveOutPrepareHeader(
                    header.AddrOfPinnedObject,
                    SIZE_OF_WAVEHEADER
                );
            if (mmResult != MMRESULT.NOERROR)
            {
                headerPool.Post(header);
                throw new WaveException(mmResult);
            }
            headerBusyPool.Add(header.AddrOfPinnedObject, header);
            dataBusyPool.Add(source.AddrOfPinnedObject, source);
            return header.AddrOfPinnedObject;
        }

        private PcmBuffer<T> Unprepare(IntPtr headerPtr)
        {
            headerBusyPool.Remove(headerPtr, out PinnedBuffer<WaveHeader> header);

            var mmResult =
                handle.waveOutUnprepareHeader(
                    headerPtr,
                    SIZE_OF_WAVEHEADER
                );
            if (mmResult != MMRESULT.NOERROR)
            {
                throw new WaveException(mmResult);
            }

            dataBusyPool.Remove(header.Target.data, out PcmBuffer<T> source);
            headerPool.Post(header);

            return source;
        }

        static public uint GetNumDevices()
        {
            return WaveOutMethod.waveOutGetNumDevs();
        }

        static public WaveOutCapabilities GetCapabilities(
            uint deviceID
        )
        {
            var caps = new WaveOutCapabilities();
            var mmResult =
                WaveOutMethod.waveOutGetDevCaps(
                    deviceID,
                    ref caps,
                    (uint)Marshal.SizeOf<WaveOutCapabilities>()
                );
            if (mmResult != MMRESULT.NOERROR)
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
                handle.waveOutWrite(
                    headerPtr,
                    SIZE_OF_WAVEHEADER
                );
            if (mmResult != MMRESULT.NOERROR)
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

            var mmResult = handle.waveOutReset();
            if (mmResult != MMRESULT.NOERROR)
            {
                throw new WaveException(mmResult);
            }
        }

        public void Execute(
            PcmBuffer<T> source,
            CancellationToken ct
        )
        {
            source.Log.Add("[wave] send start", Timer.USecDouble);
            var headerPtr = Prepare(source, ct);
            Send(headerPtr);
            source.Log.Add("[wave] send end", Timer.USecDouble);
        }

        public async Task Release(
            ITargetBlock<PcmBuffer<T>> sourceReleaseQueue,
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
                    var headerPtr = releaseQueue.Receive(ct);
                    var source = Unprepare(headerPtr);
                    source.Log.Add("[wave] unprepare", Timer.USecDouble);
                    //if (false)
                    {
                        var log = "";
                        double? temp = null;
                        source.Log.Copy().ForEach((a) => {
                            var lap = temp == null ? 0 : (a.time - temp);
                            log += $"\n[{ new DateTime((long)(a.time * 10), DateTimeKind.Utc):yyyy/MM/dd HH:mm:ss ffffff}][{a.time:0000000000.000}][{lap:0000000000.000}]{a.label}";
                            temp = a.time;
                        });
                        Logger.LogInformation($"[wave] {source.Log.GetSpentTime()} {log}");
                    }
                    sourceReleaseQueue.Post(source);
                }
                Logger.LogInformation("[wave] release loop end");
            });
        }
    }
}