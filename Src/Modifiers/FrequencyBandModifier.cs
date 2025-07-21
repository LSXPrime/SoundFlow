using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace SoundFlow.Modifiers;

/// <summary>
/// A sound modifier that implements a frequency band modifier.
/// </summary>
public class FrequencyBandModifier : SoundModifier
{
    private readonly LowPassModifier _lowPass;
    private readonly HighPassModifier _highPass;
    private readonly AudioFormat _format;

    /// <summary>
    /// Constructs a new instance of <see cref="FrequencyBandModifier"/>.
    /// </summary> 
    /// <param name="format">The audio format to process.</param>
    /// <param name="lowCutoffFrequency">The low cutoff frequency in Hertz.</param>
    /// <param name="highCutoffFrequency">The high cutoff frequency in Hertz.</param>
    public FrequencyBandModifier(AudioFormat format, float lowCutoffFrequency, float highCutoffFrequency)
    {
        _format = format; // Store the format
        _highPass = new HighPassModifier(format, lowCutoffFrequency);
        _lowPass = new LowPassModifier(format, highCutoffFrequency);
    }

    /// <summary>
    /// Gets or sets the high cutoff frequency in Hertz.
    /// </summary>
    /// <value>This value ranges from 0.0 to <see cref="AudioFormat.SampleRate"/>.</value>
    public float HighCutoffFrequency
    {
        get => _lowPass.CutoffFrequency;
        set => _lowPass.CutoffFrequency = value;
    }

    /// <summary>
    /// Gets or sets the low cutoff frequency in Hertz.
    /// </summary>
    /// <value>This value ranges from 0.0 to <see cref="AudioFormat.SampleRate"/>.</value>
    public float LowCutoffFrequency
    {
        get => _highPass.CutoffFrequency;
        set => _highPass.CutoffFrequency = value;
    }

    /// <inheritdoc/>
    public override float ProcessSample(float sample, int channel)
    {
        sample = _highPass.ProcessSample(sample, channel);
        sample = _lowPass.ProcessSample(sample, channel);
        return sample;
    }
}