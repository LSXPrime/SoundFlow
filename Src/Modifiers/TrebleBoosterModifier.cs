using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;

namespace SoundFlow.Modifiers;

/// <summary>
/// Boosts treble frequencies using a resonant high-pass filter.
/// </summary>
public class TrebleBoosterModifier : SoundModifier
{
    private readonly float[] _hpState;
    private readonly float[] _previousInput;
    private readonly AudioFormat _format;
    private float _boostGain; // Linear gain
    private float _cutoff;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrebleBoosterModifier"/> class.
    /// </summary>
    /// <param name="format">The audio format to process.</param>
    /// <param name="cutoff">The cutoff frequency of the high-pass filter.</param>
    /// <param name="boostGainDb">The gain of the boost in decibels.</param>
    public TrebleBoosterModifier(AudioFormat format, float cutoff = 4000f, float boostGainDb = 6f)
    {
        _format = format;
        _cutoff = Math.Min(20000, cutoff);
        _boostGain = MathF.Pow(10, boostGainDb / 20f);
        _hpState = new float[format.Channels];
        _previousInput = new float[format.Channels];
    }
    
    /// <summary>
    /// Gets or sets the gain of the treble boost in decibels.
    /// </summary>
    [ControllableParameter("Boost", 0.0, 24.0)]
    public float BoostGainDb
    {
        get => 20 * MathF.Log10(_boostGain);
        set => _boostGain = MathF.Pow(10, value / 20f);
    }

    /// <summary>
    /// Gets or sets the cutoff frequency of the high-pass filter.
    /// </summary>
    [ControllableParameter("Cutoff", 1000.0, 20000.0, MappingScale.Logarithmic)]
    public float Cutoff
    {
        get => _cutoff;
        set => _cutoff = Math.Min(20000, value);
    }
    
    /// <inheritdoc />
    public override void ProcessMidiMessage(MidiMessage message)
    {
        if (message.Command != MidiCommand.ControlChange) return;
        
        var value = message.ControllerValue / 127.0f;

        switch (message.ControllerNumber)
        {
            case 16: // General Purpose Controller 3 for Treble Cutoff
                var minLog = MathF.Log(1000.0f);
                var maxLog = MathF.Log(20000.0f);
                Cutoff = MathF.Exp(minLog + (maxLog - minLog) * value);
                break;
            case 17: // General Purpose Controller 4 for Treble Boost
                BoostGainDb = value * 24.0f; // Map 0-127 to 0-24 dB
                break;
        }
    }

    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel)
    {
        // 1-pole high-pass with resonance
        var dt = _format.InverseSampleRate;
        var rc = 1f / (2 * MathF.PI * _cutoff);
        var alpha = rc / (rc + dt);

        // High-pass filter
        var hp = alpha * (_hpState[channel] + sample - _previousInput[channel]);
        _hpState[channel] = hp;
        _previousInput[channel] = sample;

        // Boost and mix
        return sample + hp * _boostGain;
    }
}