using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.RTWorkQueue;
using Momiji.Interop.Wave;
using AudioClient = Momiji.Interop.AudioClient.NativeMethods;
using MMDeviceApi = Momiji.Interop.MMDeviceApi.NativeMethods;

namespace Momiji.Core.WASAPI;

public class WASAPIOut : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    private bool _disposed;

    private bool _exclusive;

    private WaveFormatExtensible _format;

    private AudioClient.IAudioClient3? _audioClient;
    private AudioClient.IAudioRenderClient? _audioRenderClient;


    private readonly RTWorkQueueManager _workQueueManager;
    private readonly EventWaitHandle _eventWaitHandle;

    private WASAPIOut(
        RTWorkQueueManager workQueueManager,
        AudioClient.IAudioClient3 audioClient,
        ILoggerFactory loggerFactory
    )
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = _loggerFactory.CreateLogger<WASAPIOut>();

        _audioClient = audioClient;
        _workQueueManager = workQueueManager;
        _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
    }

    ~WASAPIOut()
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
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
        }

        if (_audioRenderClient != default)
        {
            var count = Marshal.FinalReleaseComObject(_audioRenderClient);
            _audioRenderClient = null;
            _logger.LogInformation($"_audioRenderClient FinalReleaseComObject {count}");
        }

        if (_audioClient != default)
        {
            var count = Marshal.FinalReleaseComObject(_audioClient);
            _audioClient = null;
            _logger.LogInformation($"_audioClient FinalReleaseComObject {count}");
        }

        _eventWaitHandle.Dispose();

        _disposed = true;
        _logger.LogInformation("Dispose");
    }

    [ClassInterface(ClassInterfaceType.None)]
    private class ActivateAudioInterfaceCompletionHandler : MMDeviceApi.IActivateAudioInterfaceCompletionHandler
    {
        private readonly Action<MMDeviceApi.IActivateAudioInterfaceAsyncOperation> _action;

        public ActivateAudioInterfaceCompletionHandler(
            Action<MMDeviceApi.IActivateAudioInterfaceAsyncOperation> action
        )
        {
            _action = action;
        }

        public int ActivateCompleted(MMDeviceApi.IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            _action(activateOperation);
            return 0;
        }
    }

    private static Task<T?> ActivateAsync<T>(
        string deviceInterfacePath
    ) where T : notnull //, AudioClient.IAudioClient
    {
        var guid = typeof(T).GUID;

        var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.AttachedToParent);

        var h = new ActivateAudioInterfaceCompletionHandler((activateOperation) => {
            try
            {
                Marshal.ThrowExceptionForHR(activateOperation.GetActivateResult(out var activateResult, out var activatedInterface));
                Marshal.ThrowExceptionForHR(activateResult);

                tcs.SetResult((T?)activatedInterface);
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        Marshal.ThrowExceptionForHR(MMDeviceApi.ActivateAudioInterfaceAsync(
            deviceInterfacePath,
            ref guid,
            nint.Zero, // activationParams はIAudioClientでは使わない
            h, // TODO AsAgileは要る？
            out var activationOperation
        ));

        return tcs.Task;
    }

    public static async Task<WASAPIOut> ActivateAsync(
        RTWorkQueueManager workQueueManager,
        string deviceInterfacePath,
        ILoggerFactory loggerFactory
    )
    {
        var audioClient = await ActivateAsync<AudioClient.IAudioClient3>(deviceInterfacePath);
        return new WASAPIOut(workQueueManager, audioClient!, loggerFactory);
    }

    private WaveFormatExtensible MakeFormatSupported(
        AudioClient.AUDCLNT_SHAREMODE shareMode,
        ushort channels = 2,
        uint samplesPerSecond = 48000
    )
    {
        var format = new WaveFormatExtensible();
        format.wfe.formatType = WaveFormatEx.FORMAT.EXTENSIBLE;
        format.wfe.channels = channels;
        format.wfe.samplesPerSecond = samplesPerSecond;
        format.wfe.bitsPerSample = (ushort)(Marshal.SizeOf<float>() * 8);
        format.wfe.blockAlign = (ushort)(format.wfe.channels * format.wfe.bitsPerSample / 8);
        format.wfe.averageBytesPerSecond = format.wfe.samplesPerSecond * format.wfe.blockAlign;
        format.wfe.size = (ushort)(Marshal.SizeOf<WaveFormatExtensiblePart>());

        format.exp.validBitsPerSample = format.wfe.bitsPerSample;
        format.exp.channelMask = WaveFormatExtensiblePart.SPEAKER.FRONT_LEFT | WaveFormatExtensiblePart.SPEAKER.FRONT_RIGHT;
        format.exp.subFormat = new Guid("00000003-0000-0010-8000-00aa00389b71"); //MEDIASUBTYPE_IEEE_FLOAT

        _logger.LogInformation($"""
                format:{format}
                """);

        {
            Marshal.ThrowExceptionForHR(_audioClient!.GetMixFormat(out var ptr));

            var format2 = Marshal.PtrToStructure<WaveFormatExtensible>(ptr);
            Marshal.FreeCoTaskMem(ptr);

            _logger.LogInformation($"""
                    GetMixFormat:{format2}
                    """);
        }

        {
            Marshal.ThrowExceptionForHR(_audioClient!.IsFormatSupported(
                shareMode,
                ref format,
                out var ptr
            ));

            if (ptr != nint.Zero) //S_FALSE が返ってきてる前提
            {
                var format2 = Marshal.PtrToStructure<WaveFormatExtensible>(ptr);
                Marshal.FreeCoTaskMem(ptr);

                _logger.LogInformation($"""
                    IsFormatSupported S_FALSE:{format2}
                    """);

                format = format2;
            }
        }

        return format;
    }

    private T GetService<T>()
    {
        var guid = typeof(T).GUID;

        Marshal.ThrowExceptionForHR(_audioClient!.GetService(
            ref guid,
            out var o
        ));

        return (T)o!;
    }


    public void Initialize(
        bool exclusive,
        bool lowLatency,
        bool offload
    )
    {
        _exclusive = exclusive;

        var sharemode = _exclusive ? AudioClient.AUDCLNT_SHAREMODE.EXCLUSIVE : AudioClient.AUDCLNT_SHAREMODE.SHARED;

        foreach (var category in Enum.GetValues<AudioClient.AUDIO_STREAM_CATEGORY>())
        {
            var result = _audioClient!.IsOffloadCapable(category, out var pbOffloadCapable);
            _logger.LogInformation($"""
                category:{category}
                pbOffloadCapable:{pbOffloadCapable}
                error:{Marshal.GetPInvokeErrorMessage(result)}
                """);
        }

        {
            var audioClientProperties = new AudioClient.AudioClientProperties
            {
                cbSize = (uint)Marshal.SizeOf<AudioClient.AudioClientProperties>(),
                bIsOffload = offload,
                eCategory = AudioClient.AUDIO_STREAM_CATEGORY.Media,
                Options = AudioClient.AUDCLNT_STREAMOPTIONS.MATCH_FORMAT
            };

            var result = _audioClient!.SetClientProperties(ref audioClientProperties);
            _logger.LogInformation($"SetClientProperties:{Marshal.GetPInvokeErrorMessage(result)}");
        }

        _format = MakeFormatSupported(sharemode);

        if (lowLatency)
        {
            Marshal.ThrowExceptionForHR(_audioClient!.GetSharedModeEnginePeriod(ref _format, out var pDefaultPeriodInFrames, out var pFundamentalPeriodInFrames, out var pMinPeriodInFrames, out var pMaxPeriodInFrames));
            _logger.LogInformation($"""
                pDefaultPeriodInFrames:{pDefaultPeriodInFrames}
                pFundamentalPeriodInFrames:{pFundamentalPeriodInFrames}
                pMinPeriodInFrames:{pMinPeriodInFrames}
                pMaxPeriodInFrames:{pMaxPeriodInFrames}
                """);

            Marshal.ThrowExceptionForHR(_audioClient!.InitializeSharedAudioStream(
                AudioClient.AUDCLNT_STREAMFLAGS.STREAMFLAGS_EVENTCALLBACK
                | AudioClient.AUDCLNT_STREAMFLAGS.STREAMFLAGS_NOPERSIST,
                pMinPeriodInFrames,
                ref _format
            ));
        }
        else
        {
            Marshal.ThrowExceptionForHR(_audioClient!.Initialize(
                sharemode,
                AudioClient.AUDCLNT_STREAMFLAGS.STREAMFLAGS_EVENTCALLBACK
                | AudioClient.AUDCLNT_STREAMFLAGS.STREAMFLAGS_NOPERSIST,
                30 * 10000,
                0,
                ref _format
            ));
        }


        Marshal.ThrowExceptionForHR(_audioClient.SetEventHandle(_eventWaitHandle.SafeWaitHandle));

        _audioRenderClient = GetService<AudioClient.IAudioRenderClient>();


        Marshal.ThrowExceptionForHR(_audioClient.GetBufferSize(out var pNumBufferFrames));
        Marshal.ThrowExceptionForHR(_audioClient.GetCurrentPadding(out var pNumPaddingFrames));
        Marshal.ThrowExceptionForHR(_audioClient.GetDevicePeriod(out var phnsDefaultDevicePeriod, out var phnsMinimumDevicePeriod));
        Marshal.ThrowExceptionForHR(_audioClient.GetStreamLatency(out var pNumStreamLatency));

        _logger.LogInformation($"""
                pNumBufferFrames:{pNumBufferFrames}
                pNumPaddingFrames:{pNumPaddingFrames}
                phnsDefaultDevicePeriod:{phnsDefaultDevicePeriod}
                phnsMinimumDevicePeriod:{phnsMinimumDevicePeriod}
                pNumStreamLatency:{pNumStreamLatency}
                """);

        Marshal.ThrowExceptionForHR(_audioClient.GetMixFormat(out var ptr));
        if (ptr != nint.Zero)
        {
            _format = Marshal.PtrToStructure<WaveFormatExtensible>(ptr);
            Marshal.FreeCoTaskMem(ptr);

            _logger.LogInformation($"""
                GetMixFormat:{_format}
                """);
        }

        if (offload)
        {
            /*Marshal.ThrowExceptionForHR*/ var result = (_audioClient.GetBufferSizeLimits(ref _format, true, out var phnsMinBufferDuration, out var phnsMaxBufferDuration));
            _logger.LogInformation($"""
                phnsMinBufferDuration:{phnsMinBufferDuration}
                phnsMaxBufferDuration:{phnsMaxBufferDuration}
                error:{Marshal.GetPInvokeErrorMessage(result)}
                """);
        }
    }

    public void Start(
        Func<nint, int, int> process, 
        CancellationToken ct = default
    )
    {
        Action? put = null;

        var action = () =>
        {
            if (ct.IsCancellationRequested)
            {
                _logger.LogInformation("canceled.");
                return;
            }
            Process(process);
            put!();
        };

        put = () =>
        {
            _workQueueManager.PutWaitingWorkItem(
                0,
                _eventWaitHandle,
                action,
                null,
                ct
            );
        };

        put();

        Marshal.ThrowExceptionForHR(_audioClient!.Start());
    }

    public void Stop()
    {
        //TODO cancel
        Marshal.ThrowExceptionForHR(_audioClient!.Stop());
    }

    public void Reset()
    {
        Marshal.ThrowExceptionForHR(_audioClient!.Reset());
    }

    private void Process(Func<nint, int, int> func)
    {
        Marshal.ThrowExceptionForHR(_audioClient!.GetBufferSize(out var pNumBufferFrames));
        Marshal.ThrowExceptionForHR(_audioClient.GetCurrentPadding(out var pNumPaddingFrames));

        var numFramesRequested = pNumBufferFrames - pNumPaddingFrames;

        _logger.LogTrace($"GetBuffer:{numFramesRequested} (pNumBufferFrames:{pNumBufferFrames} pNumPaddingFrames:{pNumPaddingFrames})");
        Marshal.ThrowExceptionForHR(_audioRenderClient!.GetBuffer(
            numFramesRequested,
            out var ppData
        ));

        if (ppData == nint.Zero)
        {
            return;
        }

        var written = func(ppData, (int)numFramesRequested);

        //TODO blockAlignで割らないと動作しない？
        var numFramesWritten = written / _format.wfe.blockAlign;

        _logger.LogTrace($"ReleaseBuffer:{numFramesWritten}");
        Marshal.ThrowExceptionForHR(_audioRenderClient.ReleaseBuffer(
            (uint)numFramesWritten,
            AudioClient.AUDCLNT_BUFFERFLAGS.NONE
        ));
    }

}