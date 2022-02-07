using Microsoft.Extensions.Logging;
using Momiji.Core.Dll;
using Momiji.Core.SharedMemory;
using Momiji.Core.Timer;
using Momiji.Interop.Vst;
using Momiji.Interop.Vst.AudioMaster;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Momiji.Core.Vst
{
    public class AudioMaster<T> : IDisposable where T : struct
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private ElapsedTimeCounter Counter { get; }
        internal IDllManager DllManager { get; }

        private bool disposed;
        private readonly IDictionary<IntPtr, Effect<T>> effectMap = new ConcurrentDictionary<IntPtr, Effect<T>>();

        private readonly IPCBuffer<VstHostParam> param;

        public int SamplingRate {
            get
            {
                var p = param.AsSpan(0, 1);
                return p[0].samplingRate;
            }
        }
        public int BlockSize
        {
            get
            {
                var p = param.AsSpan(0, 1);
                return p[0].blockSize;
            }
        }

        public AudioMaster(
            int samplingRate,
            int blockSize,
            ILoggerFactory loggerFactory,
            ElapsedTimeCounter counter,
            IDllManager dllManager
        )
        {
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<AudioMaster<T>>();
            Counter = counter;
            DllManager = dllManager;

            param = new("vstTimeInfo", 1, LoggerFactory);
            var p = param.AsSpan(0, 1);
            p[0].vstTimeInfo.samplePos = 0.0;
            p[0].vstTimeInfo.sampleRate = samplingRate;
            p[0].vstTimeInfo.nanoSeconds = 0.0;
            p[0].vstTimeInfo.ppqPos = 0.0;
            p[0].vstTimeInfo.tempo = 240.0;
            p[0].vstTimeInfo.barStartPos = 0.0;
            p[0].vstTimeInfo.cycleStartPos = 0.0;
            p[0].vstTimeInfo.cycleEndPos = 0.0;
            p[0].vstTimeInfo.timeSigNumerator = 4;
            p[0].vstTimeInfo.timeSigDenominator = 4;
            p[0].vstTimeInfo.smpteOffset = 0;
            p[0].vstTimeInfo.smpteFrameRate = VstTimeInfo.VstSmpteFrameRate.kVstSmpte24fps;
            p[0].vstTimeInfo.samplesToNextClock = 0;
            p[0].vstTimeInfo.flags = VstTimeInfo.VstTimeInfoFlags.kVstTempoValid | VstTimeInfo.VstTimeInfoFlags.kVstNanosValid;

            p[0].vstProcessLevels = VstProcessLevels.kVstProcessLevelRealtime;
            p[0].samplingRate = samplingRate;
            p[0].blockSize = blockSize;
        }

        ~AudioMaster()
        {
            Dispose(false);
        }

        public IEffect<T> AddEffect(string? library)
        {
            ArgumentNullException.ThrowIfNull(library);
            if (library.Length == 0)
            {
                throw new ArgumentNullException(nameof(library));
            }

            var effect = new Effect<T>(library, this, LoggerFactory, Counter);
            effectMap.Add(effect._aeffectPtr, effect);
            effect.Open();

            return effect;
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
                Logger.LogInformation($"[vst host] stop [{effectMap.Count}]");
                foreach (var (ptr, effect) in effectMap)
                {
                    Logger.LogInformation($"[vst] try stop [{ptr}]");
                    effect.Dispose();
                }
                effectMap.Clear();
                param.Dispose();
            }

            disposed = true;
        }

        internal IntPtr AudioMasterCallBackProc(
            IntPtr/*AEffect^*/		aeffectPtr,
            Opcodes opcode,
            int index,
            IntPtr value,
            IntPtr ptr,
            float opt
        )
        {
            /*
            Logger.LogInformation(
                $"AudioMasterCallBackProc " +
                $"{nameof(aeffectPtr)}:{aeffectPtr:X} " +
                $"{nameof(opcode)}:{opcode:F} " +
                $"{nameof(index)}:{index} " +
                $"{nameof(value)}:{value:X} " +
                $"{nameof(ptr)}:{ptr:X} " +
                $"{nameof(opt)}:{opt}"
            );
            */

            switch (opcode)
            {
                case Opcodes.audioMasterVersion:
                    {
                        return new IntPtr(2400);
                    }
                case Opcodes.audioMasterGetTime:
                    {
                        var p = param.AsSpan(0, 1);
                        p[0].vstTimeInfo.nanoSeconds = Counter.NowTicks * 100;
                        return param.GetIntPtr(0);
                    }
                case Opcodes.audioMasterGetSampleRate:
                    {
                        return new IntPtr(SamplingRate);
                    }
                case Opcodes.audioMasterGetBlockSize:
                    {
                        return new IntPtr(BlockSize);
                    }
                case Opcodes.audioMasterGetCurrentProcessLevel:
                    {
                        var p = param.AsSpan(0, 1);
                        return new IntPtr((int)p[0].vstProcessLevels);
                    }

                default:
                    //Logger.LogInformation("NOP");
                    return default;
            }
        }
    }

}