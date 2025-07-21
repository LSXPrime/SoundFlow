using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace SoundFlow.Modifiers;

/// <summary>
/// A dynamic range compressor modifier.
/// </summary>
public class CompressorModifier : SoundModifier
{
    /// <summary>
    /// The threshold level in dBFS (-inf to 0).
    /// </summary>
    public float ThresholdDb { get; set; }
    
    /// <summary>
    /// The compression ratio (1:1 to inf:1).
    /// </summary>
    public float Ratio { get; set; }
    
    /// <summary>
    /// The attack time in milliseconds.
    /// </summary>
    public float AttackMs { get; set; }
    
    /// <summary>
    /// The release time in milliseconds.
    /// </summary>
    public float ReleaseMs { get; set; }
    
    /// <summary>
    /// The knee radius in dBFS. A knee radius of 0 is a hard knee.
    /// </summary>
    public float KneeDb { get; set; }
    
    /// <summary>
    /// The make-up gain in dBFS.
    /// </summary>
    public float MakeupGainDb { get; set; }

    private float _envelope;
    private float _gain;
    private readonly AudioFormat _format;

    /// <summary>
    /// Constructs a new instance of <see cref="CompressorModifier"/>.
    /// </summary>
    /// <param name="format">The audio format to process.</param>
    /// <param name="thresholdDb">The threshold level in dBFS (-inf to 0).</param>
    /// <param name="ratio">The compression ratio (1:1 to inf:1).</param>
    /// <param name="attackMs">The attack time in milliseconds.</param>
    /// <param name="releaseMs">The release time in milliseconds.</param>
    /// <param name="kneeDb">The knee width in dB (0 for hard knee).</param>
    /// <param name="makeupGainDb">The makeup gain in dB.</param>
    public CompressorModifier(AudioFormat format, float thresholdDb, float ratio, float attackMs, float releaseMs, float kneeDb = 0, float makeupGainDb = 0)
    {
        _format = format;
        ThresholdDb = thresholdDb;
        Ratio = ratio;
        AttackMs = attackMs;
        ReleaseMs = releaseMs;
        KneeDb = kneeDb;
        MakeupGainDb = makeupGainDb;
        _gain = 1f;
    }
    
    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel)
    {
        // Convert to dB
        var sampleDb = LinearToDb(MathF.Abs(sample));
        
        // Calculate envelope with different attack/release
        var alphaA = MathF.Exp(-1f / (AttackMs * 0.001f * _format.SampleRate));
        var alphaR = MathF.Exp(-1f / (ReleaseMs * 0.001f * _format.SampleRate));
        
        _envelope = sampleDb > _envelope 
            ? alphaA * _envelope + (1 - alphaA) * sampleDb
            : alphaR * _envelope + (1 - alphaR) * sampleDb;

        // Calculate gain reduction
        var overshootDb = _envelope - ThresholdDb;
        var reductionDb = 0f;

        // Logarithmic Soft Knee
        if (overshootDb > 0)
            reductionDb = KneeDb > 0
                ? (Ratio - 1) / Ratio * KneeDb * MathF.Log10(1 + overshootDb / KneeDb)
                : overshootDb * (Ratio - 1) / Ratio; // Hard knee (or if kneeDb <= 0, treat as hard knee)

        // Smooth gain changes
        var targetGain = DbToLinear(-reductionDb + MakeupGainDb);
        var alpha = reductionDb == 0 ? alphaR : alphaA;
        _gain = alpha * _gain + (1 - alpha) * targetGain;

        return sample * _gain;
    }

    private static float DbToLinear(float db) => MathF.Pow(10, db / 20f);
    private static float LinearToDb(float linear) => 20f * MathF.Log10(linear);
}