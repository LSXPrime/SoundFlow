using SoundFlow.Abstracts;

namespace SoundFlow.Modifiers;

/// <summary>
/// A sound modifier that implements a delay effect.
/// </summary>
public sealed class DelayModifier : SoundModifier
{
    private readonly List<float[]> _delayLines;
    private readonly int[] _delayIndices;
    private readonly float[] _filterStates;

    /// <summary>
    /// The feedback amount (0.0 - 1.0).
    /// </summary>
    public float Feedback { get; set; }

    /// <summary>
    /// The wet/dry mix (0.0 - 1.0).
    /// </summary>
    public float WetMix { get; set; }

    /// <summary>
    /// The cutoff frequency in Hertz.
    /// </summary>
    public float Cutoff { get; set; }
    
    /// <summary>
    /// Constructs a new instance of <see cref="DelayModifier"/>.
    /// </summary>
    /// <param name="delaySamples">The length of the delay line in samples.</param>
    /// <param name="feedback">The feedback amount (0.0 - 1.0).</param>
    /// <param name="wetMix">The wet/dry mix (0.0 - 1.0).</param>
    /// <param name="cutoff">The cutoff frequency in Hertz.</param>
    public DelayModifier(int delaySamples = 44100, float feedback = 0.5f, 
        float wetMix = 0.3f, float cutoff = 5000f)
    {
        Feedback = feedback;
        WetMix = wetMix;
        Cutoff = cutoff;

        _delayLines = [];
        _delayIndices = new int[AudioEngine.Channels];
        _filterStates = new float[AudioEngine.Channels];
        
        for(var i = 0; i < AudioEngine.Channels; i++)
        {
            _delayLines.Add(new float[delaySamples]);
        }
    }

    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel)
    {
        var delayLine = _delayLines[channel];
        var index = _delayIndices[channel];
        
        // Get delayed sample
        var delayed = delayLine[index];
        
        // Apply low-pass filter to feedback
        var rc = 1f / (2 * MathF.PI * Cutoff);
        var alpha = AudioEngine.Instance.InverseSampleRate / (rc + AudioEngine.Instance.InverseSampleRate);
        delayed = alpha * delayed + (1 - alpha) * _filterStates[channel];
        _filterStates[channel] = delayed;
        
        // Write to delay line
        delayLine[index] = sample + delayed * Feedback;
        _delayIndices[channel] = (index + 1) % delayLine.Length;

        return sample * (1 - WetMix) + delayed * WetMix;
    }
}