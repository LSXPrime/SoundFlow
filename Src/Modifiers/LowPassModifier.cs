using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace SoundFlow.Modifiers;

/// <summary>
/// A sound modifier that implements a low-pass filter.
/// </summary>
public class LowPassModifier : SoundModifier
{
    private readonly float[] _previousOutput;
    private float _cutoffFrequency;
    private readonly AudioFormat _format;

    /// <summary>
    /// Initializes a new instance of the <see cref="LowPassModifier"/> class.
    /// </summary>
    /// <param name="format">The audio format to process.</param>
    /// <param name="cutoffFrequency">The cutoff frequency of the filter.</param>
    public LowPassModifier(AudioFormat format, float cutoffFrequency)
    {
        _previousOutput = new float[format.Channels];
        CutoffFrequency = cutoffFrequency;
        _format = format;
    }

    /// <summary>
    /// Gets or sets the cutoff frequency of the filter.
    /// </summary>
    public float CutoffFrequency
    {
        get => _cutoffFrequency;
        set => _cutoffFrequency = Math.Max(20, value); // Minimum 20Hz
    }

    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel)
    {
        var dt = _format.InverseSampleRate;
        var rc = 1f / (2 * MathF.PI * _cutoffFrequency);
        var alpha = dt / (rc + dt);
        _previousOutput[channel] += alpha * (sample - _previousOutput[channel]);
        return _previousOutput[channel];
    }
}