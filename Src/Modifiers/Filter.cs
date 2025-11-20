using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;

namespace SoundFlow.Modifiers;

/// <summary>
/// Implements a digital biquad filter, allowing for various filter types such as LowPass, HighPass, BandPass, and Notch.
/// </summary>
public class Filter : SoundModifier
{
    /// <summary>
    /// Defines the different types of filters available.
    /// </summary>
    public enum FilterType
    {
        /// <summary>
        /// Allows frequencies below the cutoff frequency to pass, attenuating frequencies above it.
        /// </summary>
        LowPass,
        /// <summary>
        /// Allows frequencies above the cutoff frequency to pass, attenuating frequencies below it.
        /// </summary>
        HighPass,
        /// <summary>
        /// Allows frequencies around the cutoff frequency to pass, attenuating frequencies further away.
        /// </summary>
        BandPass,
        /// <summary>
        /// Attenuates frequencies around the cutoff frequency, allowing frequencies further away to pass.
        /// </summary>
        Notch
    }

    // Parameters
    private FilterType _type = FilterType.LowPass;
    
    /// <summary>
    /// Gets or sets the audio format.
    /// </summary>
    public AudioFormat Format { get; set; }

    /// <summary>
    /// Gets or sets the type of filter.
    /// Changing the filter type recalculates the filter coefficients.
    /// </summary>
    [ControllableParameter("Filter Type", 0, 3)]
    public FilterType Type
    {
        get => _type;
        set
        {
            _type = value;
            CalculateCoefficients();
        }
    }

    private float _cutoffFrequency = 1000f;

    /// <summary>
    /// Gets or sets the cutoff frequency of the filter in Hertz.
    /// This frequency determines the point at which the filter starts to attenuate the signal.
    /// Changing the cutoff frequency recalculates the filter coefficients.
    /// </summary>
    [ControllableParameter("Cutoff", 20.0, 20000.0, MappingScale.Logarithmic)]
    public float CutoffFrequency
    {
        get => _cutoffFrequency;
        set
        {
            _cutoffFrequency = value;
            CalculateCoefficients();
        }
    }

    private float _resonance = 0.7f;

    /// <summary>
    /// Gets or sets the resonance of the filter, a value between 0 and 1.
    /// Higher resonance values emphasize frequencies around the cutoff frequency, potentially leading to self-oscillation in some filter types.
    /// Changing the resonance recalculates the filter coefficients. Resonance is clamped between 0.01 and 0.99 to prevent instability.
    /// </summary>
    [ControllableParameter("Resonance", 0.0, 1.0)]
    public float Resonance
    {
        get => _resonance;
        set
        {
            _resonance = value;
            CalculateCoefficients();
        }
    }

    // Internal state for the biquad filter
    private float _x1, _x2, _y1, _y2; // Delay elements for input (x) and output (y) samples
    private float _a0, _a1, _a2, _b1, _b2; // Filter coefficients for the biquad filter structure

    /// <summary>
    /// Initializes a new instance of the <see cref="Filter"/> class with default settings and calculates initial filter coefficients.
    /// </summary>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    public Filter(AudioFormat format)
    {
        Format = format;
        CalculateCoefficients();
    }

    /// <inheritdoc/>
    public override string Name { get; set; } = "Filter";

    /// <inheritdoc/>
    public override void ProcessMidiMessage(MidiMessage message)
    {
        if (message.Command != MidiCommand.ControlChange) return;

        switch (message.ControllerNumber)
        {
            // Standard CC for Filter Cutoff (Brightness)
            case 74:
                // Map MIDI value (0-127) to logarithmic frequency range (20Hz - 20kHz)
                var normalizedCutoff = message.ControllerValue / 127.0f;
                var minLog = MathF.Log(20.0f);
                var maxLog = MathF.Log(20000.0f);
                CutoffFrequency = MathF.Exp(minLog + (maxLog - minLog) * normalizedCutoff);
                break;
            
            // Standard CC for Filter Resonance (Timbre/Harmonic Content)
            case 71:
                // Map MIDI value (0-127) to resonance range (0.0 - 1.0)
                Resonance = message.ControllerValue / 127.0f;
                break;
        }
    }

    /// <inheritdoc/>
    public override float ProcessSample(float sample, int channel)
    {
        var output = _a0 * sample + _a1 * _x1 + _a2 * _x2 - _b1 * _y1 - _b2 * _y2;

        // Update delay elements for the next sample
        _x2 = _x1;
        _x1 = sample;
        _y2 = _y1;
        _y1 = output;

        return output;
    }

    /// <summary>
    /// Calculates the biquad filter coefficients based on the current <see cref="Type"/>, <see cref="CutoffFrequency"/>, and <see cref="Resonance"/> parameters.
    /// This method uses standard formulas for digital biquad filter coefficient calculation and normalizes the coefficients.
    /// </summary>
    private void CalculateCoefficients()
    {
        float sampleRate = Format.SampleRate;
        _resonance = Math.Clamp(_resonance, 0.01f, 0.99f);
        var omega = 2.0f * MathF.PI * CutoffFrequency / sampleRate; // Angular frequency
        var sinOmega = MathF.Sin(omega);
        var cosOmega = MathF.Cos(omega);
        var alpha = sinOmega / (2 * Resonance);

        // Calculate coefficients based on the selected filter type
        switch (Type)
        {
            case FilterType.LowPass:
                _a0 = (1 - cosOmega) / 2;
                _a1 = 1 - cosOmega;
                _a2 = (1 - cosOmega) / 2;
                break;
            case FilterType.HighPass:
                _a0 = (1 + cosOmega) / 2;
                _a1 = -(1 + cosOmega);
                _a2 = (1 + cosOmega) / 2;
                break;
            case FilterType.BandPass:
                _a0 = alpha;
                _a1 = 0;
                _a2 = -alpha;
                break;
            case FilterType.Notch:
                _a0 = 1;
                _a1 = -2 * cosOmega;
                _a2 = 1;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        _b1 = -2 * cosOmega;
        _b2 = 1 - alpha;

        // Normalize coefficients by dividing by a0 (which is actually 'a0' in biquad formulas, and in our case it's (1+alpha) after calculations)
        var a0Inv = 1 / (1 + alpha);
        _a0 *= a0Inv;
        _a1 *= a0Inv;
        _a2 *= a0Inv;
        _b1 *= a0Inv;
        _b2 *= a0Inv;
    }
}