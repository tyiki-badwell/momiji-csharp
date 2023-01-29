using System.Diagnostics.CodeAnalysis;

namespace Momiji.Core.Configuration;

public record class Param
{
    public int BufferCount { get; init; }
    public bool Local { get; init; }
    public bool Connect { get; init; }

    public int Width { get; init; }
    public int Height { get; init; }
    public int TargetBitrate { get; init; }
    public float MaxFrameRate { get; init; }
    public int IntraFrameIntervalUs { get; init; }

    [DisallowNull] 
    public string? EffectName { get; init; }
    public int SamplingRate { get; init; }
    public float SampleLength { get; init; }
    /*
        この式を満たさないとダメ
        new_size = blockSize
        Fs = samplingRate

        if (frame_size<Fs/400)
        return -1;
        if (400*new_size!=Fs   && 200*new_size!=Fs   && 100*new_size!=Fs   &&
            50*new_size!=Fs   &&  25*new_size!=Fs   &&  50*new_size!=3*Fs &&
            50*new_size!=4*Fs &&  50*new_size!=5*Fs &&  50*new_size!=6*Fs)
        return -1;

    0.0025
    0.005
    0.01
    0.02
    0.04
    0.06
    0.08
    0.1
    0.12
        */
}
