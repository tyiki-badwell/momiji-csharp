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

        public WaveException()
        {
        }

        public WaveException(string message) : base(message)
        {
        }

        public WaveException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public WaveException(MMRESULT mmResult) : base(MakeMessage(mmResult))
        {
        }

        static private string MakeMessage(MMRESULT mmResult)
        {
            var text = new System.Text.StringBuilder(256);
            SafeNativeMethods.waveOutGetErrorText(mmResult, text, (uint)text.Capacity);
            return $"{text.ToString()}({mmResult})";
        }
    }

    public class PcmBuffer<T> : PinnedBufferWithLog<T[]> where T : struct
    {
        public PcmBuffer(int blockSize, int channels) : base(new T[blockSize * channels])
        {
        }
    }

    public sealed class WaveOutShort : WaveOut<short>
    {
        public WaveOutShort(
            uint deviceID,
            ushort channels,
            uint samplesPerSecond,
            WaveFormatExtensiblePart.SPEAKER channelMask,
            ILoggerFactory loggerFactory,
            Timer timer,
            ITargetBlock<PcmBuffer<short>> sourceReleaseQueue
        ) : base(
            deviceID,
            channels,
            samplesPerSecond,
            channelMask,
            new Guid("00000001-0000-0010-8000-00aa00389b71"),
            loggerFactory,
            timer,
            sourceReleaseQueue
            )
        { }
    }

    public sealed class WaveOutFloat : WaveOut<float>
    {
        public WaveOutFloat(
            uint deviceID,
            ushort channels,
            uint samplesPerSecond,
            WaveFormatExtensiblePart.SPEAKER channelMask,
            ILoggerFactory loggerFactory,
            Timer timer,
            ITargetBlock<PcmBuffer<float>> sourceReleaseQueue
        ) : base(
            deviceID,
            channels,
            samplesPerSecond,
            channelMask,
            new Guid("00000003-0000-0010-8000-00aa00389b71"),
            loggerFactory,
            timer,
            sourceReleaseQueue
            )
        { }
    }

    internal class WaveHeaderBuffer : PinnedBuffer<WaveHeader>
    {
        internal WaveHeaderBuffer() : base(new WaveHeader())
        {
        }
    }

    public class WaveOut<T> : IDisposable where T : struct
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private Timer Timer { get; }

        private bool disposed = false;

        private BufferPool<WaveHeaderBuffer> headerPool;
        private readonly TransformBlock<IntPtr, PcmBuffer<T>> releaseAction;

        private readonly IDictionary<IntPtr, WaveHeaderBuffer> headerBusyPool = new ConcurrentDictionary<IntPtr, WaveHeaderBuffer>();
        private readonly IDictionary<IntPtr, PcmBuffer<T>> dataBusyPool = new ConcurrentDictionary<IntPtr, PcmBuffer<T>>();

        private PinnedDelegate<DriverCallBack.Proc> driverCallBack;
        private WaveOut handle;

        private static readonly int SIZE_OF_T = Marshal.SizeOf<T>();
        private static readonly uint SIZE_OF_WAVEHEADER = (uint)Marshal.SizeOf<WaveHeader>();

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
                Logger.LogDebug($"[wave] WOM_DONE {dw1}");
                releaseAction.SendAsync(dw1);
            }
        }

        public WaveOut(
            uint deviceID,
            ushort channels,
            uint samplesPerSecond,
            WaveFormatExtensiblePart.SPEAKER channelMask,
            Guid formatSubType,
            ILoggerFactory loggerFactory,
            Timer timer,
            ITargetBlock<PcmBuffer<T>> releaseQueue
        )
        {
            LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            Timer = timer ?? throw new ArgumentNullException(nameof(timer));
            if (releaseQueue == default)
            {
                throw new ArgumentNullException(nameof(releaseQueue));
            }

            Logger = LoggerFactory.CreateLogger<WaveOut<T>>();
            headerPool = new BufferPool<WaveHeaderBuffer>(1, () => { return new WaveHeaderBuffer(); }, LoggerFactory);
            driverCallBack = new PinnedDelegate<DriverCallBack.Proc>(new DriverCallBack.Proc(DriverCallBackProc));

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

            //たまに失敗するので、ピン止めしておく
            using var formatPin = new PinnedBuffer<WaveFormatExtensible>(format);

            var mmResult =
                SafeNativeMethods.waveOutOpen(
                    out handle,
                    deviceID,
                    ref format,
                    driverCallBack.FunctionPointer,
                    IntPtr.Zero,
                    (
                          DriverCallBack.TYPE.FUNCTION
                        | DriverCallBack.TYPE.WAVE_FORMAT_DIRECT
                    )
                );
            if (mmResult != MMRESULT.NOERROR)
            {
                throw new WaveException(mmResult);
            }

            releaseAction = new TransformBlock<IntPtr, PcmBuffer<T>>(headerPtr => Unprepare(headerPtr));
            releaseAction.LinkTo(releaseQueue);
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
                        Thread.Sleep(1000);
                        Logger.LogInformation($"[wave] wait busy buffers :{headerBusyPool.Count}");
                    }
                    Logger.LogInformation($"[wave] wait end :{headerBusyPool.Count}");

                    releaseAction.Complete();
                    releaseAction.Completion.Wait();

                    handle.Close();
                }
                handle = null;
            }

            headerPool?.Dispose();
            headerPool = null;

            driverCallBack?.Dispose();
            driverCallBack = null;

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
                headerPool.SendAsync(header);
                throw new WaveException(mmResult);
            }
            headerBusyPool.Add(header.AddrOfPinnedObject, header);
            dataBusyPool.Add(source.AddrOfPinnedObject, source);

            Logger.LogDebug($"[wave] prepare [{header.AddrOfPinnedObject}] [{dataBusyPool.Count}]");
            return header.AddrOfPinnedObject;
        }

        private PcmBuffer<T> Unprepare(IntPtr headerPtr)
        {
            headerBusyPool.Remove(headerPtr, out var header);
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
            headerPool.SendAsync(header);

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug($"[wave] unprepare [{headerPtr}][{dataBusyPool.Count}]");
                source.Log.Add("[wave] unprepare", Timer.USecDouble);
                var log = "";
                double? temp = null;
                source.Log.Copy().ForEach((a) =>
                {
                    var lap = temp.HasValue ? (a.time - temp) : 0;
                    log += $"\n[{ new DateTime((long)(a.time * 10), DateTimeKind.Utc):yyyy/MM/dd HH:mm:ss ffffff}][{a.time:0000000000.000}][{lap:0000000000.000}]{a.label}";
                    temp = a.time;
                });
                Logger.LogDebug($"[wave] {source.Log.GetSpentTime()} {log}");
                Logger.LogDebug($"[wave] release [{source.AddrOfPinnedObject}]");
            }

            return source;
        }

        private void Send(IntPtr headerPtr)
        {
            var mmResult =
                handle.waveOutWrite(
                    headerPtr,
                    SIZE_OF_WAVEHEADER
                );
            if (mmResult != MMRESULT.NOERROR)
            {
                releaseAction.SendAsync(headerPtr);
                throw new WaveException(mmResult);
            }
        }

        private void Reset()
        {
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
            if (
                handle.IsInvalid
            || handle.IsClosed
            )
            {
                return;
            }

            if (source == default)
            {
                throw new ArgumentNullException(nameof(source));
            }

            Logger.LogDebug($"[wave] execute [{source.AddrOfPinnedObject}]");
            source.Log.Add("[wave] send start", Timer.USecDouble);
            var headerPtr = Prepare(source, ct);
            Send(headerPtr);
            source.Log.Add("[wave] send end", Timer.USecDouble);
        }
    }
}