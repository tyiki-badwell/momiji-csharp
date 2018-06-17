using Momiji.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Dataflow;
using System.Collections.Generic;
using System.Diagnostics;

namespace Momiji
{
    namespace Core
    {
        namespace Wave
        {
            public class Out
            {
                private bool disposed = false;

                private BufferBlock<PinnedBuffer<Interop.Wave.WaveHeader>> headerPool;
                private Dictionary<IntPtr, PinnedBuffer<Interop.Wave.WaveHeader>> headerBusyPool;

                private Interop.Wave.WaveOut handle;

                private void DriverCallBackProc(
                    IntPtr hdrvr,
                    Interop.Wave.DriverCallBack.MM_EXT_WINDOW_MESSAGE uMsg,
                    IntPtr dwUser,
                    IntPtr dw1,
                    IntPtr dw2
                )
                {
                    try
                    {
                        Unprepare(dw1);
                    }
                    catch (Exception e)
                    {
                    }
                }

                public Out(
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

                    format.exp.validBitsPerSample = format.wfe.bitsPerSample;   //TODO: 実際にハードウェアでサポートしている限界に揃える
                    format.exp.channelMask = channelMask;
                    format.exp.subFormat = formatSubType;

                    headerPool = new BufferBlock<PinnedBuffer<Interop.Wave.WaveHeader>>();
                    headerPool.Post(new PinnedBuffer<Interop.Wave.WaveHeader>(new Interop.Wave.WaveHeader()));

                    headerBusyPool = new Dictionary<IntPtr, PinnedBuffer<Interop.Wave.WaveHeader>>();

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
                        throw new Exception($"{mmResult}");
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


                private IntPtr Prepare(IntPtr data, System.UInt32 useSize)
                {
                    var header = headerPool.Receive();
                    {
                        var waveHeader = header.Target();
                        waveHeader.data = data;
                        waveHeader.bufferLength = useSize;
                        waveHeader.flags = (Interop.Wave.WaveHeader.FLAG.BEGINLOOP | Interop.Wave.WaveHeader.FLAG.ENDLOOP);
                        waveHeader.loops = 1;

                        waveHeader.bytesRecorded = 0;
                        waveHeader.user = IntPtr.Zero;
                        waveHeader.next = IntPtr.Zero;
                        waveHeader.reserved = IntPtr.Zero;
                    }

                    var mmResult =
                        Interop.Wave.waveOutPrepareHeader(
                            ref handle,
                            header.AddrOfPinnedObject(),
                            (uint)Marshal.SizeOf<Interop.Wave.WaveHeader>()
                        );
                    if (mmResult != Interop.Wave.MMRESULT.NOERROR)
                    {
                        headerPool.Post(header);
                        throw new Exception($"{mmResult}");
                    }
                    return header.AddrOfPinnedObject();
                }

                private void Unprepare(IntPtr headerPtr)
                {
                    var header = headerBusyPool[headerPtr];

                    var mmResult =
                        Interop.Wave.waveOutUnprepareHeader(
                            ref handle,
                            headerPtr,
                            (uint)Marshal.SizeOf<Interop.Wave.WaveHeader>()
                        );
                    if (mmResult != Interop.Wave.MMRESULT.NOERROR)
                    {
                        throw new Exception($"{mmResult}");
                    }

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
                        throw new Exception($"{mmResult}");
                    }
                    return caps;
                }

                public void Send(IntPtr data, System.UInt32 useSize)
                {
                    if (
                        handle.IsInvalid
                    || handle.IsClosed
                    )
                    {
                        return;
                    }

                    IntPtr headerPtr = Prepare(data, useSize);

                    var mmResult =
                        Interop.Wave.waveOutWrite(
                            ref handle,
                            headerPtr,
                            (uint)Marshal.SizeOf<Interop.Wave.WaveHeader>()
                        );
                    if (mmResult != Interop.Wave.MMRESULT.NOERROR)
                    {
                        Unprepare(headerPtr);
                        throw new Exception($"{mmResult}");
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

                    var mmResult = Interop.Wave.waveOutReset(ref handle);
                    if (mmResult != Interop.Wave.MMRESULT.NOERROR)
                    {
                        throw new Exception($"{mmResult}");
                    }
                }
            }
        }
    }
}