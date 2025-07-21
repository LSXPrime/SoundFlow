using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace SoundFlow.Components;

/// <summary>
/// Implements a digital biquad filter, allowing for various filter types such as LowPass, HighPass, BandPass, and Notch.
/// </summary>
public class Filter : SoundComponent
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
    /// Gets or sets the type of filter.
    /// Changing the filter type recalculates the filter coefficients.
    /// </summary>
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
    /// <param name="engine">The parent audio engine.</param>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    public Filter(AudioEngine engine, AudioFormat format) : base(engine, format)
    {
        CalculateCoefficients();
    }

    /// <inheritdoc/>
    public override string Name { get; set; } = "Filter";

    /// <inheritdoc/>
    protected override void GenerateAudio(Span<float> buffer, int channels)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = ProcessSample(buffer[i]);
        }
    }

    /// <summary>
    /// Processes a single audio sample through the biquad filter.
    /// </summary>
    /// <param name="input">The input audio sample.</param>
    /// <returns>The filtered audio sample.</returns>
    private float ProcessSample(float input)
    {
        var output = _a0 * input + _a1 * _x1 + _a2 * _x2 - _b1 * _y1 - _b2 * _y2;

        // Update delay elements for the next sample
        _x2 = _x1;
        _x1 = input;
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
        // Clamp resonance to avoid instability at very high resonance values
        _resonance = Math.Clamp(_resonance, 0.01f, 0.99f);
        // Pre-compute common values to optimize coefficient calculations
        float sampleRate = Format.SampleRate;
        var omega = 2.0f * MathF.PI * CutoffFrequency / sampleRate; // Angular frequency
        var sinOmega = MathF.Sin(omega);
        var cosOmega = MathF.Cos(omega);
        var alpha = sinOmega / (2 * Resonance); // Bandwidth parameter, related to resonance

        // Calculate coefficients based on the selected filter type
        switch (Type)
        {
            case FilterType.LowPass:
                _a0 = (1 - cosOmega) / 2;
                _a1 = 1 - cosOmega;
                _a2 = (1 - cosOmega) / 2;
                _b1 = -2 * cosOmega;
                _b2 = 1 - alpha;
                break;
            case FilterType.HighPass:
                _a0 = (1 + cosOmega) / 2;
                _a1 = -(1 + cosOmega);
                _a2 = (1 + cosOmega) / 2;
                _b1 = -2 * cosOmega;
                _b2 = 1 - alpha;
                break;
            case FilterType.BandPass:
                _a0 = alpha;
                _a1 = 0;
                _a2 = -alpha;
                _b1 = -2 * cosOmega;
                _b2 = 1 - alpha;
                break;
            case FilterType.Notch:
                _a0 = 1;
                _a1 = -2 * cosOmega;
                _a2 = 1;
                _b1 = -2 * cosOmega;
                _b2 = 1 - alpha;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        // Normalize coefficients by dividing by a0 (which is actually 'a0' in biquad formulas, and in our case it's (1+alpha) after calculations)
        var a0Inv = 1 / (1 + alpha);
        _a0 *= a0Inv;
        _a1 *= a0Inv;
        _a2 *= a0Inv;
        _b1 *= a0Inv;
        _b2 *= a0Inv;
    }
}
