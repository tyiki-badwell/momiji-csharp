using Microsoft.Extensions.Logging;
using Momiji.Core.SharedMemory;
using Momiji.Interop.Vst;
using Momiji.Interop.Vst.AudioMaster;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Momiji.Core.Vst
{
    public class VstHostException : Exception
    {
        public VstHostException()
        {
        }

        public VstHostException(string message) : base(message)
        {
        }

        public VstHostException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    internal struct VstHostParam
    {
        public VstTimeInfo vstTimeInfo;
        public VstProcessLevels vstProcessLevels;
        public int samplingRate;
        public int blockSize;
    }

    public class VstHost : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        private bool disposed;

        private IPCBuffer<VstHostParam> param;

        private readonly IList<EffectProxy> effectList = new List<EffectProxy>();

        public int SamplingRate
        {
            get
            {
                var p = param.AsSpan(0, 1);
                return p[0].samplingRate;
            }

            set
            {
                //TODO 動作中はエラー
                var p = param.AsSpan(0, 1);
                p[0].samplingRate = value;
            }

        }
        public int BlockSize
        {
            get
            {
                var p = param.AsSpan(0, 1);
                return p[0].blockSize;
            }

            set
            {
                //TODO 動作中はエラー
                var p = param.AsSpan(0, 1);
                p[0].blockSize = value;
            }
        }

        public VstHost(
            int samplingRate,
            int blockSize,
            ILoggerFactory loggerFactory
        )
        {
            LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            Logger = LoggerFactory.CreateLogger<VstHost>();

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

        ~VstHost()
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
                foreach (var effect in effectList)
                {
                    effect.Dispose();
                }
                effectList.Clear();

                param?.Dispose();
                param = default;

                Logger.LogInformation($"[vst host] disposing");
            }



            disposed = true;
        }

        public EffectProxy AddEffect(string library, bool is64)
        {
            var effect = new EffectProxy(this, library, is64, LoggerFactory);
            effectList.Add(effect);
            return effect;
        }


        public void Start()
        {
            foreach (var effect in effectList)
            {
                effect.Start();
            }
        }

        public void Stop()
        {
            foreach (var effect in effectList)
            {
                effect.Stop();
            }
        }

    }


    public class EffectProxy : IDisposable
    {
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        private VstHost Parent { get; }

        private bool disposed;

        public EffectProxy(
            VstHost vstHost,
            string library,
            bool is64,
            ILoggerFactory loggerFactory
        )
        {
            LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            Logger = LoggerFactory.CreateLogger<EffectProxy>();
            Parent = vstHost;





        }

        ~EffectProxy()
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
                Logger.LogInformation($"[vst effect proxy] disposing");
            }



            disposed = true;
        }

        internal void Start()
        {

        }

        internal void Stop()
        {

        }
    }



}