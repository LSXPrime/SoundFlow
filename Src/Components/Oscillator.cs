using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace SoundFlow.Components;

/// <summary>
/// Generates various types of high-quality, band-limited audio waveforms at a specified frequency and amplitude.
/// </summary>
public class Oscillator : SoundComponent
{
    /// <summary>
    /// Defines the different types of waveforms the oscillator can generate.
    /// </summary>
    public enum WaveformType
    {
        /// <summary>
        /// A pure sine wave, known for its smooth and fundamental tone.
        /// </summary>
        Sine,

        /// <summary>
        /// A band-limited square wave, rich in odd harmonics, producing a bright, clean sound without digital aliasing.
        /// </summary>
        Square,

        /// <summary>
        /// A band-limited sawtooth wave, containing both even and odd harmonics, resulting in a bright, clean sound without digital aliasing.
        /// </summary>
        Sawtooth,

        /// <summary>
        /// A triangle wave, containing only odd harmonics.
        /// Note: This implementation is not band-limited, but aliasing is less pronounced than with square or saw waves.
        /// </summary>
        Triangle,

        /// <summary>
        /// Generates random noise across all frequencies, useful for creating percussive sounds or textures.
        /// </summary>
        Noise,

        /// <summary>
        /// A band-limited pulse wave (also known as a rectangular wave), similar to a square wave but with adjustable pulse width for timbral variations.
        /// </summary>
        Pulse
    }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="Oscillator"/> class, which generates audio waveforms at a specified frequency and amplitude.
    /// </summary>
    /// <param name="engine">The parent audio engine.</param>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    public Oscillator(AudioEngine engine, AudioFormat format) : base(engine, format) 
    {
        Frequency = 440f; // A4 Note
    }

    // Parameters
    private float _frequency;
    private float _phaseIncrement;

    /// <summary>
    /// Gets or sets the frequency of the oscillator in Hertz.
    /// This determines the pitch of the generated sound.
    /// </summary>
    public float Frequency
    {
        get => _frequency;
        set
        {
            _frequency = value;
            _phaseIncrement = (2.0f * MathF.PI * _frequency / Format.SampleRate);
        }
    }

    /// <summary>
    /// Gets or sets the amplitude of the oscillator, controlling the loudness of the generated sound.
    /// Typically ranges from 0 to 1, but can exceed 1 for overdrive effects if used in later processing stages.
    /// </summary>
    public float Amplitude { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the type of waveform the oscillator will generate.
    /// See <see cref="WaveformType"/> for available waveform options.
    /// </summary>
    public WaveformType Type { get; set; } = WaveformType.Sine;

    /// <summary>
    /// Gets or sets the phase offset of the waveform in radians (from 0 to 2*PI).
    /// This can be used to synchronize multiple oscillators or create stereo effects.
    /// </summary>
    public float PhaseOffset { get; set; } = 0f;

    /// <summary>
    /// Gets or sets the pulse width for the <see cref="WaveformType.Pulse"/> waveform, as a fraction of the cycle (0 to 1).
    /// A value of 0.5 results in a square wave. This parameter is only effective when <see cref="Type"/> is set to <see cref="WaveformType.Pulse"/>.
    /// </summary>
    public float PulseWidth { get; set; } = 0.5f;

    // Internal state
    private float _currentPhase;
    private readonly Random _random = new();

    /// <inheritdoc/>
    public override string Name { get; set; } = "Oscillator";

    /// <inheritdoc/>
    protected override void GenerateAudio(Span<float> buffer, int channels)
    {
        var frameCount = buffer.Length / channels;

        for (var frame = 0; frame < frameCount; frame++)
        {
            // Generate a single sample for this time frame.
            var sample = GenerateSample();

            // Write the same mono sample to all channels for the current frame.
            for (var channel = 0; channel < channels; channel++)
            {
                buffer[frame * channels + channel] = sample;
            }
        }
    }

    /// <summary>
    /// Generates a single audio sample based on the current waveform type, phase, and amplitude.
    /// This method updates the internal phase for the next sample.
    /// </summary>
    /// <returns>The generated audio sample value.</returns>
    private float GenerateSample()
    {
        // Calculate the effective phase for this sample, including the offset, and wrap it to the [0, 2*PI) range.
        var effectivePhase = _currentPhase + PhaseOffset;
        while (effectivePhase >= 2.0f * MathF.PI)
        {
            effectivePhase -= 2.0f * MathF.PI;
        }

        float sampleValue;
        
        // Use normalized phase (0 to 1) and increment for PolyBLEP calculations
        var normalizedPhase = effectivePhase / (2.0f * MathF.PI);
        var normalizedIncrement = _phaseIncrement / (2.0f * MathF.PI);

        switch (Type)
        {
            case WaveformType.Sine:
                sampleValue = MathF.Sin(effectivePhase);
                break;
            
            case WaveformType.Sawtooth:
                // Naive sawtooth is (2.0 * phase) - 1.0
                sampleValue = (2.0f * normalizedPhase) - 1.0f;
                // Subtract the PolyBLEP correction at the discontinuity
                sampleValue -= Polyblep(normalizedPhase, normalizedIncrement);
                break;

            case WaveformType.Square:
            case WaveformType.Pulse:
                // A pulse wave can be generated by subtracting two sawtooth waves.
                // This automatically makes it band-limited if the sawtooth is.
                var pulseWidth = Type == WaveformType.Square ? 0.5f : PulseWidth;
                var t2 = normalizedPhase - pulseWidth;
                if (t2 < 0f) t2 += 1.0f;

                // First sawtooth
                var s1 = (2.0f * normalizedPhase) - 1.0f;
                s1 -= Polyblep(normalizedPhase, normalizedIncrement);

                // Second, phase-shifted sawtooth
                var s2 = (2.0f * t2) - 1.0f;
                s2 -= Polyblep(t2, normalizedIncrement);

                sampleValue = s1 - s2;
                break;
            
            case WaveformType.Triangle:
                // Naive triangle wave, aliasing is less of an issue than with saw/square
                sampleValue = 2.0f * MathF.Abs(2.0f * normalizedPhase - 1.0f) - 1.0f;
                break;

            case WaveformType.Noise:
                sampleValue = (float)(_random.NextDouble() * 2.0 - 1.0);
                break;

            default:
                sampleValue = 0f;
                break;
        }
        
        // Update the internal phase for the next sample and keep it within the [0, 2*PI) range.
        _currentPhase += _phaseIncrement;
        while (_currentPhase >= 2.0f * MathF.PI)
        {
            _currentPhase -= 2.0f * MathF.PI;
        }

        return sampleValue * Amplitude;
    }

    /// <summary>
    /// Calculates the polynomial correction for a band-limited step function (PolyBLEP).
    /// This is used to remove aliasing from waveforms with sharp discontinuities.
    /// </summary>
    /// <param name="t">The normalized phase of the oscillator (0 to 1).</param>
    /// <param name="dt">The normalized phase increment per sample.</param>
    /// <returns>The correction value to be subtracted from the naive waveform.</returns>
    private float Polyblep(float t, float dt)
    {
        // Around the start of the cycle (t=0)
        if (t < dt)
        {
            t /= dt;
            return t + t - t * t - 1.0f;
        }
        // Around the end of the cycle (t=1)
        if (t > 1.0f - dt)
        {
            t = (t - 1.0f) / dt;
            return t * t + t + t + 1.0f;
        }
        // No correction needed elsewhere
        return 0.0f;
    }
}