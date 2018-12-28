﻿using Microsoft.Extensions.Logging;
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

    class WaveHeaderBuffer : PinnedBuffer<WaveHeader>
    {
        int Wrote { get; set; }

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
        private BufferBlock<IntPtr> releaseQueue = new BufferBlock<IntPtr>();

        private IDictionary<IntPtr, WaveHeaderBuffer> headerBusyPool = new ConcurrentDictionary<IntPtr, WaveHeaderBuffer>();
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
                Logger.LogDebug($"[wave] WOM_DONE {dw1}");
                releaseQueue.SendAsync(dw1);
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

            headerPool = new BufferPool<WaveHeaderBuffer>(1, () => { return new WaveHeaderBuffer(); }, LoggerFactory);

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
            headerBusyPool.Remove(headerPtr, out WaveHeaderBuffer header);

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

            Logger.LogDebug($"[wave] unprepare [{headerPtr}][{dataBusyPool.Count}]");
            return source;
        }

        private void Send(IntPtr headerPtr)
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

        private void Reset()
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
            Logger.LogDebug($"[wave] execute [{source.AddrOfPinnedObject}]");
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
                    if (Logger.IsEnabled(LogLevel.Debug))
                    {
                        var log = "";
                        double? temp = null;
                        source.Log.Copy().ForEach((a) => {
                            var lap = temp == null ? 0 : (a.time - temp);
                            log += $"\n[{ new DateTime((long)(a.time * 10), DateTimeKind.Utc):yyyy/MM/dd HH:mm:ss ffffff}][{a.time:0000000000.000}][{lap:0000000000.000}]{a.label}";
                            temp = a.time;
                        });
                        Logger.LogDebug($"[wave] {source.Log.GetSpentTime()} {log}");
                    }
                    Logger.LogDebug($"[wave] release [{source.AddrOfPinnedObject}]");
                    sourceReleaseQueue.SendAsync(source);
                }
                Logger.LogInformation("[wave] release loop end");
            });
        }
    }
}