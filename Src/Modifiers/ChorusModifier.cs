using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace SoundFlow.Modifiers;

/// <summary>
/// A sound modifier that implements a chorus effect.
/// </summary>
public sealed class ChorusModifier : SoundModifier
{
    /// <summary>
    /// The depth of the chorus effect in milliseconds.
    /// </summary>
    public float DepthMs { get; set; }

    /// <summary>
    /// The rate of the LFO modulation in Hz.
    /// </summary>
    public float RateHz { get; set; }

    /// <summary>
    /// The feedback amount (0.0 - 1.0).
    /// </summary>
    public float Feedback { get; }

    /// <summary>
    /// The wet/dry mix (0.0 - 1.0).
    /// </summary>
    public float WetDryMix { get; set; }

    private readonly List<float[]> _delayLines;
    private readonly float[] _lfoPhases;
    private readonly int[] _delayIndices;
    private readonly int _maxDelaySamples;
    private readonly AudioFormat _format;

    /// <summary>
    /// Constructs a new instance of <see cref="ChorusModifier"/>.
    /// </summary>
    /// <param name="format">The audio format to process.</param>
    /// <param name="depthMs">The depth of the chorus effect in milliseconds.</param>
    /// <param name="rateHz">The rate of the LFO modulation in Hz.</param>
    /// <param name="feedback">The feedback amount (0.0 - 1.0).</param>
    /// <param name="wetDryMix">The wet/dry mix (0.0 - 1.0).</param>
    /// <param name="maxDelayMs">The maximum delay time in milliseconds. Will be converted to samples.</param>
    public ChorusModifier(AudioFormat format, float depthMs = 2f, float rateHz = 0.5f, float feedback = 0.7f, float wetDryMix = 0.5f, float maxDelayMs = 50f)
    {
        _format = format;
        DepthMs = Math.Max(0, depthMs);
        RateHz = Math.Max(0, rateHz);
        Feedback = Math.Clamp(feedback, 0f, 1f);
        WetDryMix = Math.Clamp(wetDryMix, 0f, 1f);
        _maxDelaySamples = Math.Max(1, (int)(maxDelayMs * _format.SampleRate / 1000f));

        _delayLines = [];
        _delayIndices = new int[_format.Channels];
        _lfoPhases = new float[_format.Channels];

        for (int i = 0; i < _format.Channels; i++)
        {
            _delayLines.Add(new float[_maxDelaySamples]);
        }
    }

    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel)
    {
        var delayLine = _delayLines[channel];
        var phase = _lfoPhases[channel];

        // Calculate modulated delay time in samples
        var lfo = MathF.Sin(phase) * DepthMs * _format.SampleRate / 1000f;
        var delayTimeSamples = (_maxDelaySamples / 2f) + lfo;
        delayTimeSamples = Math.Clamp(delayTimeSamples, 1, _maxDelaySamples -1); // Ensure delayTimeSamples is within valid range

        // Get delayed sample (No Interpolation for now, can be added later)
        var readIndex = (_delayIndices[channel] - (int)delayTimeSamples + _maxDelaySamples) % _maxDelaySamples;
        var delayed = delayLine[readIndex];

        // Update delay line with feedback
        delayLine[_delayIndices[channel]] = sample + delayed * Feedback;
        _delayIndices[channel] = (_delayIndices[channel] + 1) % _maxDelaySamples;

        // Update LFO phase
        _lfoPhases[channel] += MathF.Tau * RateHz / _format.SampleRate;
        if (_lfoPhases[channel] >= MathF.Tau) _lfoPhases[channel] -= MathF.Tau;

        return sample * (1 - WetDryMix) + delayed * WetDryMix;
    }
}