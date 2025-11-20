using SoundFlow.Abstracts;
using SoundFlow.Interfaces;
using SoundFlow.Structs;
using System;

namespace SoundFlow.Modifiers;

/// <summary>
/// A dynamic range compressor modifier.
/// </summary>
public class CompressorModifier : SoundModifier
{
    private float _thresholdDb;
    private float _ratio;
    private float _attackMs;
    private float _releaseMs;
    private float _kneeDb;
    private float _makeupGainDb;

    /// <summary>
    /// The threshold level in dBFS (-inf to 0).
    /// </summary>
    [ControllableParameter("Threshold", -60.0, 0.0)]
    public float ThresholdDb
    {
        get => _thresholdDb;
        set { _thresholdDb = value; UpdateParameters(); }
    }

    /// <summary>
    /// The compression ratio (1:1 to inf:1).
    /// </summary>
    [ControllableParameter("Ratio", 1.0, 20.0)]
    public float Ratio
    {
        get => _ratio;
        set { _ratio = value; UpdateParameters(); }
    }

    /// <summary>
    /// The attack time in milliseconds.
    /// </summary>
    [ControllableParameter("Attack", 0.1, 200.0, MappingScale.Logarithmic)]
    public float AttackMs
    {
        get => _attackMs;
        set { _attackMs = value; UpdateParameters(); }
    }

    /// <summary>
    /// The release time in milliseconds.
    /// </summary>
    [ControllableParameter("Release", 5.0, 2000.0, MappingScale.Logarithmic)]
    public float ReleaseMs
    {
        get => _releaseMs;
        set { _releaseMs = value; UpdateParameters(); }
    }

    /// <summary>
    /// The knee radius in dBFS. A knee radius of 0 is a hard knee.
    /// </summary>
    [ControllableParameter("Knee", 0.0, 12.0)]
    public float KneeDb
    {
        get => _kneeDb;
        set { _kneeDb = value; UpdateParameters(); }
    }

    /// <summary>
    /// The make-up gain in dBFS.
    /// </summary>
    [ControllableParameter("Makeup Gain", 0.0, 24.0)]
    public float MakeupGainDb
    {
        get => _makeupGainDb;
        set { _makeupGainDb = value; UpdateParameters(); }
    }

    // Per-channel state
    private readonly float[] _envelope;

    // Calculated coefficients
    private float _alphaA;
    private float _alphaR;
    
    private readonly AudioFormat _format;
    private const float Epsilon = 1e-12f; // Prevents log(0)

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
        _envelope = new float[format.Channels];
        
        Array.Fill(_envelope, LinearToDb(Epsilon));
        
        _thresholdDb = thresholdDb;
        _ratio = ratio;
        _attackMs = attackMs;
        _releaseMs = releaseMs;
        _kneeDb = kneeDb;
        _makeupGainDb = makeupGainDb;

        UpdateParameters();
    }

    /// <summary>
    /// Call this method whenever you change the public properties.
    /// </summary>
    public void UpdateParameters()
    {
        // Clamp attack/release to a small minimum to prevent division by zero and clicks.
        var attackS = Math.Max(0.0001f, _attackMs * 0.001f);
        var releaseS = Math.Max(0.0001f, _releaseMs * 0.001f);
        
        _alphaA = MathF.Exp(-1f / (attackS * _format.SampleRate));
        _alphaR = MathF.Exp(-1f / (releaseS * _format.SampleRate));
    }
    
    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel)
    {
        // Peak Detection (Envelope Follower)
        var sampleDb = LinearToDb(MathF.Abs(sample));
        
        // Determine if we are in attack or release phase and apply the appropriate smoothing
        var currentEnvelope = _envelope[channel];
        _envelope[channel] = sampleDb > currentEnvelope 
            ? _alphaA * currentEnvelope + (1 - _alphaA) * sampleDb
            : _alphaR * currentEnvelope + (1 - _alphaR) * sampleDb;

        // Gain Computation
        var reductionDb = 0f;
        var overshootDb = _envelope[channel] - _thresholdDb;

        // If the envelope is over the threshold, calculate the required reduction
        if (overshootDb > 0) 
            reductionDb = overshootDb * (1f - 1f / _ratio); //  hard-knee compression formula

        // Convert the desired dB change (reduction + makeup) to a linear gain multiplier
        var targetGainLinear = DbToLinear(-reductionDb + _makeupGainDb);

        // Apply the gain to the original sample
        return sample * targetGainLinear;
    }

    private static float DbToLinear(float db) => MathF.Pow(10, db / 20f);
    private static float LinearToDb(float linear) => 20f * MathF.Log10(linear + Epsilon);
}