using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace SoundFlow.Modifiers;

/// <summary>
/// Boosts bass frequencies using a resonant low-pass filter.
/// </summary>
public class BassBoosterModifier : SoundModifier
{
    /// <summary>
    /// Gets or sets the cutoff frequency in Hertz.
    /// </summary>
    public float Cutoff { get; set; }

    /// <summary>
    /// Gets or sets the boost gain in decibels.
    /// </summary>
    public float BoostGain { get; set; }

    private readonly float[] _lpState;
    private readonly float[] _resonanceState;
    private readonly AudioFormat _format;

    /// <summary>
    /// Initializes a new instance of the <see cref="BassBoosterModifier"/> class.
    /// </summary>
    /// <param name="format">The audio format to process.</param>
    /// <param name="cutoff">The cutoff frequency in Hertz.</param>
    /// <param name="boostGain">The boost gain in decibels.</param>
    public BassBoosterModifier(AudioFormat format, float cutoff = 150f, float boostGain = 6f)
    {
        _format = format;
        Cutoff = Math.Max(20, cutoff); // Minimum 20Hz
        BoostGain = MathF.Pow(10, boostGain / 20f); // Convert dB to linear
        _lpState = new float[format.Channels];
        _resonanceState = new float[format.Channels];
    }

    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel)
    {
        // 1-pole low-pass with resonance
        var dt = _format.InverseSampleRate;
        var rc = 1f / (2 * MathF.PI * Cutoff);
        var alpha = dt / (rc + dt);

        // Low-pass filter
        _lpState[channel] += alpha * (sample - _lpState[channel]);

        // Add resonance feedback
        var feedbackFactor = 0.5f * BoostGain;
        feedbackFactor = Math.Min(0.95f, feedbackFactor); // Clamp to a max value less than 1
        _resonanceState[channel] = _lpState[channel] + _resonanceState[channel] * feedbackFactor;
        
        // Mix boosted bass with original
        return sample + _resonanceState[channel];
    }
}