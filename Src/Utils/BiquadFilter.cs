using System.Runtime.CompilerServices;
using SoundFlow.Enums;

namespace SoundFlow.Utils;

/// <summary>
/// A standalone Biquad filter implementation.
/// Handles state and coefficient calculation for a single audio channel based on the RBJ Audio EQ Cookbook.
/// </summary>
public sealed class BiquadFilter
{
    // Coefficients
    private float _a1, _a2, _b0, _b1, _b2;
    
    // State (Previous inputs/outputs)
    private float _x1, _x2, _y1, _y2;

    /// <summary>
    /// Updates the filter coefficients based on the specified parameters.
    /// </summary>
    /// <param name="type">The type of filter to apply.</param>
    /// <param name="sampleRate">The sample rate of the audio data.</param>
    /// <param name="frequency">The center or cutoff frequency in Hz.</param>
    /// <param name="q">The quality factor (Q) or resonance.</param>
    /// <param name="gainDb">The gain in decibels (used for Peaking and Shelf filters).</param>
    /// <param name="shelfSlope">The shelf slope parameter (used for Shelf filters).</param>
    public void Update(FilterType type, float sampleRate, float frequency, float q, float gainDb = 0f, float shelfSlope = 1f)
    {
        // Clamp frequency to prevent stability issues near Nyquist
        frequency = Math.Clamp(frequency, 10f, sampleRate * 0.49f);
        
        // Ensure Q is valid to avoid division by zero
        q = Math.Max(0.01f, q);

        var omega = 2.0f * MathF.PI * frequency / sampleRate;
        var sinOmega = MathF.Sin(omega);
        var cosOmega = MathF.Cos(omega);
        var alpha = sinOmega / (2.0f * q);
        
        // A is used for gain calculations
        var a = MathF.Pow(10, gainDb / 40); 
        float a0;

        switch (type)
        {
            case FilterType.LowPass:
                _b0 = (1 - cosOmega) / 2;
                _b1 = 1 - cosOmega;
                _b2 = (1 - cosOmega) / 2;
                a0 = 1 + alpha;
                _a1 = -2 * cosOmega;
                _a2 = 1 - alpha;
                break;

            case FilterType.HighPass:
                _b0 = (1 + cosOmega) / 2;
                _b1 = -(1 + cosOmega);
                _b2 = (1 + cosOmega) / 2;
                a0 = 1 + alpha;
                _a1 = -2 * cosOmega;
                _a2 = 1 - alpha;
                break;

            case FilterType.BandPass:
                _b0 = alpha;
                _b1 = 0;
                _b2 = -alpha;
                a0 = 1 + alpha;
                _a1 = -2 * cosOmega;
                _a2 = 1 - alpha;
                break;

            case FilterType.Notch:
                _b0 = 1;
                _b1 = -2 * cosOmega;
                _b2 = 1;
                a0 = 1 + alpha;
                _a1 = -2 * cosOmega;
                _a2 = 1 - alpha;
                break;

            case FilterType.Peaking:
                _b0 = 1 + alpha * a;
                _b1 = -2 * cosOmega;
                _b2 = 1 - alpha * a;
                a0 = 1 + alpha / a;
                _a1 = -2 * cosOmega;
                _a2 = 1 - alpha / a;
                break;

            case FilterType.LowShelf:
            {
                var sqrtA = MathF.Sqrt(a);
                var alphaShelf = sinOmega / 2 * MathF.Sqrt((a + 1 / a) * (1 / shelfSlope - 1) + 2);
                
                _b0 = a * ((a + 1) - (a - 1) * cosOmega + 2 * sqrtA * alphaShelf);
                _b1 = 2 * a * ((a - 1) - (a + 1) * cosOmega);
                _b2 = a * ((a + 1) - (a - 1) * cosOmega - 2 * sqrtA * alphaShelf);
                a0 = (a + 1) + (a - 1) * cosOmega + 2 * sqrtA * alphaShelf;
                _a1 = -2 * ((a - 1) + (a + 1) * cosOmega);
                _a2 = (a + 1) + (a - 1) * cosOmega - 2 * sqrtA * alphaShelf;
                break;
            }

            case FilterType.HighShelf:
            {
                var sqrtA = MathF.Sqrt(a);
                var alphaShelf = sinOmega / 2 * MathF.Sqrt((a + 1 / a) * (1 / shelfSlope - 1) + 2);

                _b0 = a * ((a + 1) + (a - 1) * cosOmega + 2 * sqrtA * alphaShelf);
                _b1 = -2 * a * ((a - 1) + (a + 1) * cosOmega);
                _b2 = a * ((a + 1) + (a - 1) * cosOmega - 2 * sqrtA * alphaShelf);
                a0 = (a + 1) - (a - 1) * cosOmega + 2 * sqrtA * alphaShelf;
                _a1 = 2 * ((a - 1) - (a + 1) * cosOmega);
                _a2 = (a + 1) - (a - 1) * cosOmega - 2 * sqrtA * alphaShelf;
                break;
            }

            case FilterType.AllPass:
                _b0 = 1 - alpha;
                _b1 = -2 * cosOmega;
                _b2 = 1 + alpha;
                a0 = 1 + alpha;
                _a1 = -2 * cosOmega;
                _a2 = 1 - alpha;
                break;
            
            default:
                // Pass-through if unknown
                _b0 = 1; _b1 = 0; _b2 = 0; a0 = 1; _a1 = 0; _a2 = 0;
                break;
        }

        // Normalize coefficients by a0
        var invA0 = 1.0f / a0;
        _b0 *= invA0;
        _b1 *= invA0;
        _b2 *= invA0;
        _a1 *= invA0;
        _a2 *= invA0;
    }

    /// <summary>
    /// Processes a single audio sample through the filter.
    /// </summary>
    /// <param name="sample">The input sample.</param>
    /// <returns>The filtered output sample.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float sample)
    {
        var output = _b0 * sample + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;

        // Shift state
        _x2 = _x1;
        _x1 = sample;
        _y2 = _y1;
        _y1 = output;

        return output;
    }

    /// <summary>
    /// Resets the internal state of the filter.
    /// </summary>
    public void Reset()
    {
        _x1 = _x2 = _y1 = _y2 = 0;
    }
}