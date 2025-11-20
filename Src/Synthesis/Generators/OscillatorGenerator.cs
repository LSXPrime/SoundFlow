using SoundFlow.Components;
using SoundFlow.Interfaces;
using SoundFlow.Synthesis.Interfaces;
using SoundFlow.Synthesis.Voices;

namespace SoundFlow.Synthesis.Generators;

/// <summary>
/// An IGenerator that produces a basic waveform (e.g., sine, square).
/// </summary>
internal sealed class OscillatorGenerator : IGenerator
{
    private readonly Oscillator.WaveformType _waveformType;
    private float _phase;

    /// <summary>
    /// Initializes a new instance of the <see cref="OscillatorGenerator"/> class.
    /// </summary>
    /// <param name="waveformType">The type of waveform to generate.</param>
    public OscillatorGenerator(Oscillator.WaveformType waveformType)
    {
        _waveformType = waveformType;
    }

    /// <inheritdoc />
    public int Generate(Span<float> buffer, VoiceContext context)
    {
        var phaseIncrement = 2.0f * MathF.PI * context.Frequency / context.SampleRate;
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = GenerateSample(_phase, phaseIncrement);
            _phase += phaseIncrement;
            if (_phase >= 2.0f * MathF.PI)
            {
                _phase -= 2.0f * MathF.PI;
            }
        }
        return buffer.Length;
    }

    /// <inheritdoc />
    public void Reset()
    {
        _phase = 0.0f;
    }

    private float GenerateSample(float phase, float phaseIncrement)
    {
        var normalizedPhase = phase / (2.0f * MathF.PI);
        var normalizedIncrement = phaseIncrement / (2.0f * MathF.PI);

        return _waveformType switch
        {
            Oscillator.WaveformType.Sine => MathF.Sin(phase),
            Oscillator.WaveformType.Square => Math.Sign(MathF.Sin(phase)),
            Oscillator.WaveformType.Sawtooth => (2.0f * normalizedPhase) - 1.0f - Polyblep(normalizedPhase, normalizedIncrement),
            Oscillator.WaveformType.Triangle => 2.0f * MathF.Abs(2.0f * normalizedPhase - 1.0f) - 1.0f,
            _ => 0.0f,
        };
    }

    /// <summary>
    /// Calculates the polynomial correction for a band-limited step function (PolyBLEP).
    /// </summary>
    private static float Polyblep(float t, float dt)
    {
        if (t < dt)
        {
            t /= dt;
            return t + t - t * t - 1.0f;
        }
        if (t > 1.0f - dt)
        {
            t = (t - 1.0f) / dt;
            return t * t + t + t + 1.0f;
        }
        return 0.0f;
    }
}