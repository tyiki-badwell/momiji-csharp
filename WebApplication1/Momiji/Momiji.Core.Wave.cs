﻿using Momiji.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Dataflow;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;

namespace Momiji
{
    namespace Core
    {
        namespace Wave
        {
            public class WaveException: Exception
            {
                public WaveException(Interop.Wave.MMRESULT mmResult): base(makeMessage(mmResult))
                {   
                }

                static private string makeMessage(Interop.Wave.MMRESULT mmResult)
                {
                    var text = new System.Text.StringBuilder(256);
                    Interop.Wave.waveOutGetErrorText(mmResult, text, (uint)text.Capacity);
                    return text.ToString();
                }
            }

            public class WaveOut : IDisposable
            {
                private bool disposed = false;

                private BufferBlock<PinnedBuffer<Interop.Wave.WaveHeader>> headerPool = new BufferBlock<PinnedBuffer<Interop.Wave.WaveHeader>>();
                private Dictionary<IntPtr, PinnedBuffer<Interop.Wave.WaveHeader>> headerBusyPool = new Dictionary<IntPtr, PinnedBuffer<Interop.Wave.WaveHeader>>();
                private Dictionary<IntPtr, PinnedBuffer<float[]>> dataBusyPool = new Dictionary<IntPtr, PinnedBuffer<float[]>>();

                private Interop.Wave.WaveOut handle;

                private Task processTask;
                //TODO デリゲート経由で使えるよう直す
                ITargetBlock<PinnedBuffer<float[]>> _inputReleaseQueue;

                private async void DriverCallBackProc(
                    IntPtr hdrvr,
                    Interop.Wave.DriverCallBack.MM_EXT_WINDOW_MESSAGE uMsg,
                    IntPtr dwUser,
                    IntPtr dw1,
                    IntPtr dw2
                )
                {
                    if (uMsg == Interop.Wave.DriverCallBack.MM_EXT_WINDOW_MESSAGE.WOM_DONE)
                    {
                        await Task.Run(() =>
                        {
                            Unprepare(dw1);
                        });
                    }
                }

                public WaveOut(
                    UInt32 deviceID,
                    UInt16 channels,
                    UInt32 samplesPerSecond,
                    UInt16 bitsPerSample,
                    Interop.Wave.WaveFormatExtensiblePart.SPEAKER channelMask,
                    Guid formatSubType,
                    UInt32 samplesPerBuffer
                )
                {
                    var format = new Interop.Wave.WaveFormatExtensible();
                    format.wfe.formatType = Interop.Wave.WaveFormatEx.FORMAT.EXTENSIBLE;
                    format.wfe.channels = channels;
                    format.wfe.samplesPerSecond = samplesPerSecond;
                    format.wfe.bitsPerSample = bitsPerSample;
                    format.wfe.blockAlign = (ushort)(format.wfe.channels * format.wfe.bitsPerSample / 8);
                    format.wfe.averageBytesPerSecond = format.wfe.samplesPerSecond * format.wfe.blockAlign;
                    format.wfe.size = (ushort)(Marshal.SizeOf<Interop.Wave.WaveFormatExtensiblePart>());

                    format.exp.validBitsPerSample = format.wfe.bitsPerSample;
                    format.exp.channelMask = channelMask;
                    format.exp.subFormat = formatSubType;

                    headerPool.Post(new PinnedBuffer<Interop.Wave.WaveHeader>(new Interop.Wave.WaveHeader()));
                    headerPool.Post(new PinnedBuffer<Interop.Wave.WaveHeader>(new Interop.Wave.WaveHeader()));
                    headerPool.Post(new PinnedBuffer<Interop.Wave.WaveHeader>(new Interop.Wave.WaveHeader()));

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
                        try
                        {
                            processTask.Wait();
                        }
                        catch (AggregateException e)
                        {
                            foreach (var v in e.InnerExceptions)
                            {
                                Trace.WriteLine($"[wave] Process Exception:{e.Message} {v.Message}");
                            }
                        }

                        if (handle != null)
                        {
                            if (
                                !handle.IsInvalid
                                && !handle.IsClosed
                            )
                            {
                                Reset();

                                //バッファの開放待ち
                                var header = headerPool.Receive();
                                header.Dispose();

                                handle.Close();
                            }
                        }
                    }

                    disposed = true;
                }


                private IntPtr Prepare(PinnedBuffer<float[]> data)
                {
                    var header = headerPool.Receive();
                    {
                        var waveHeader = header.Target();
                        waveHeader.data = data.AddrOfPinnedObject();
                        waveHeader.bufferLength = (uint)data.Target().Length;
                        waveHeader.flags = (Interop.Wave.WaveHeader.FLAG.BEGINLOOP | Interop.Wave.WaveHeader.FLAG.ENDLOOP);
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
                            (uint)Marshal.SizeOf<Interop.Wave.WaveHeader>()
                        );
                    if (mmResult != Interop.Wave.MMRESULT.NOERROR)
                    {
                        headerPool.Post(header);
                        throw new WaveException(mmResult);
                    }
                    headerBusyPool.Add(header.AddrOfPinnedObject(), header);
                    dataBusyPool.Add(data.AddrOfPinnedObject(), data);
                    return header.AddrOfPinnedObject();
                }

                private void Unprepare(IntPtr headerPtr)
                {
                    PinnedBuffer<Interop.Wave.WaveHeader> header;
                    headerBusyPool.Remove(headerPtr, out header);

                    var mmResult =
                        Interop.Wave.waveOutUnprepareHeader(
                            handle,
                            headerPtr,
                            (uint)Marshal.SizeOf<Interop.Wave.WaveHeader>()
                        );
                    if (mmResult != Interop.Wave.MMRESULT.NOERROR)
                    {
                        throw new WaveException(mmResult);
                    }

                    PinnedBuffer<float[]> data;
                    dataBusyPool.Remove(header.Target().data, out data);
                    _inputReleaseQueue.Post(data);

                    headerPool.Post(header);
                }

                private Interop.Wave.WaveHeader AllocateHeader()
                {
                    return new Interop.Wave.WaveHeader();
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

                public void Send(PinnedBuffer<float[]> data)
                {
                    if (
                        handle.IsInvalid
                    || handle.IsClosed
                    )
                    {
                        return;
                    }

                    IntPtr headerPtr = Prepare(data);

                    var mmResult =
                        Interop.Wave.waveOutWrite(
                            handle,
                            headerPtr,
                            (uint)Marshal.SizeOf<Interop.Wave.WaveHeader>()
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

                public void Run(
                    ISourceBlock<PinnedBuffer<float[]>> inputQueue,
                    ITargetBlock<PinnedBuffer<float[]>> inputReleaseQueue,
                    CancellationToken ct)
                {
                    _inputReleaseQueue = inputReleaseQueue;
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
                                //Trace.WriteLine("[wave] get data");

                                Send(data);

                                //inputReleaseQueue.Post(data);
                                //Trace.WriteLine("[wave] post data");
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
    }
}