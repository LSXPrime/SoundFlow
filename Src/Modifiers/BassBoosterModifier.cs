using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;

namespace SoundFlow.Modifiers;

/// <summary>
/// Boosts bass frequencies using a resonant low-pass filter.
/// </summary>
public class BassBoosterModifier : SoundModifier
{
    private float _cutoff;
    private float _boostGain; // Linear gain

    /// <summary>
    /// Gets or sets the cutoff frequency in Hertz.
    /// </summary>
    [ControllableParameter("Cutoff", 20.0, 1000.0, MappingScale.Logarithmic)]
    public float Cutoff
    {
        get => _cutoff;
        set => _cutoff = Math.Max(20, value);
    }

    /// <summary>
    /// Gets or sets the boost gain in decibels.
    /// </summary>
    [ControllableParameter("Boost", 0.0, 24.0)]
    public float BoostGainDb
    {
        get => 20 * MathF.Log10(_boostGain);
        set => _boostGain = MathF.Pow(10, value / 20f);
    }

    private readonly float[] _lpState;
    private readonly float[] _resonanceState;
    private readonly AudioFormat _format;

    /// <summary>
    /// Initializes a new instance of the <see cref="BassBoosterModifier"/> class.
    /// </summary>
    /// <param name="format">The audio format to process.</param>
    /// <param name="cutoff">The cutoff frequency in Hertz.</param>
    /// <param name="boostGainDb">The boost gain in decibels.</param>
    public BassBoosterModifier(AudioFormat format, float cutoff = 150f, float boostGainDb = 6f)
    {
        _format = format;
        _cutoff = Math.Max(20, cutoff); // Minimum 20Hz
        _boostGain = MathF.Pow(10, boostGainDb / 20f); // Convert dB to linear
        _lpState = new float[format.Channels];
        _resonanceState = new float[format.Channels];
    }

    /// <inheritdoc />
    public override void ProcessMidiMessage(MidiMessage message)
    {
        if (message.Command != MidiCommand.ControlChange) return;
        
        var value = message.ControllerValue / 127.0f;

        switch (message.ControllerNumber)
        {
            case 14: // General Purpose Controller 1 for Bass Cutoff
                var minLog = MathF.Log(20.0f);
                var maxLog = MathF.Log(1000.0f);
                Cutoff = MathF.Exp(minLog + (maxLog - minLog) * value);
                break;
            case 15: // General Purpose Controller 2 for Bass Boost
                BoostGainDb = value * 24.0f; // Map 0-127 to 0-24 dB
                break;
        }
    }

    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel)
    {
        // 1-pole low-pass with resonance
        var dt = _format.InverseSampleRate;
        var rc = 1f / (2 * MathF.PI * _cutoff);
        var alpha = dt / (rc + dt);

        // Low-pass filter
        _lpState[channel] += alpha * (sample - _lpState[channel]);

        // Add resonance feedback
        var feedbackFactor = 0.5f * _boostGain;
        feedbackFactor = Math.Min(0.95f, feedbackFactor); // Clamp to a max value less than 1
        _resonanceState[channel] = _lpState[channel] + _resonanceState[channel] * feedbackFactor;
        
        // Mix boosted bass with original
        return sample + _resonanceState[channel];
    }
}