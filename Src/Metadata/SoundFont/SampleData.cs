namespace SoundFlow.Metadata.SoundFont;

/// <summary>
/// Holds the final, processed audio data for a single SF2 sample,
/// ready to be used by a SamplerGenerator.
/// </summary>
internal sealed class SampleData
{
    /// <summary>
    /// The name of the sample.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The processed sample data as 32-bit floats.
    /// </summary>
    public float[] Data { get; init; } = [];

    /// <summary>
    /// The loop start point in samples, adjusted for resampling.
    /// </summary>
    public uint StartLoop { get; init; }

    /// <summary>
    /// The loop end point in samples, adjusted for resampling.
    /// </summary>
    public uint EndLoop { get; init; }

    /// <summary>
    /// The original sample rate of the sample before resampling.
    /// </summary>
    public uint OriginalSampleRate { get; init; }

    /// <summary>
    /// The original root key (MIDI note number) of the sample.
    /// </summary>
    public byte RootKey { get; init; }

    /// <summary>
    /// The fine-tuning correction in cents.
    /// </summary>
    public sbyte Correction { get; init; }
}