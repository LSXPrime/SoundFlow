using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace SoundFlow.Components;

/// <summary>
/// Generates various types of audio waveforms at a specified frequency and amplitude.
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
        /// A square wave, rich in odd harmonics, producing a bright and buzzy sound.
        /// </summary>
        Square,

        /// <summary>
        /// A sawtooth wave, containing both even and odd harmonics, resulting in a bright and raspy sound.
        /// </summary>
        Sawtooth,

        /// <summary>
        /// A triangle wave, containing only odd harmonics that decrease in amplitude more rapidly than in a square wave, giving a mellow, flute-like sound.
        /// </summary>
        Triangle,

        /// <summary>
        /// Generates random noise across all frequencies, useful for creating percussive sounds or textures.
        /// </summary>
        Noise,

        /// <summary>
        /// A pulse wave (also known as a rectangular wave), similar to a square wave but with adjustable pulse width, allowing for timbral variations.
        /// </summary>
        Pulse // Optional
    }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="Oscillator"/> class, which generates audio waveforms at a specified frequency and amplitude.
    /// </summary>
    /// <param name="engine">The parent audio engine.</param>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    public Oscillator(AudioEngine engine, AudioFormat format) : base(engine, format) { }

    // Parameters
    /// <summary>
    /// Gets or sets the frequency of the oscillator in Hertz.
    /// This determines the pitch of the generated sound.
    /// </summary>
    public float Frequency { get; set; } = 440f; // A4 note

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
    /// Gets or sets the phase offset of the waveform in radians.
    /// This can be used to synchronize multiple oscillators or create stereo effects.
    /// </summary>
    public float Phase { get; set; } = 0f;

    /// <summary>
    /// Gets or sets the pulse width for the <see cref="WaveformType.Pulse"/> waveform, as a fraction of the cycle (0 to 1).
    /// A value of 0.5 results in a square wave. This parameter is only effective when <see cref="Type"/> is set to <see cref="WaveformType.Pulse"/>.
    /// </summary>
    public float PulseWidth { get; set; } = 0.5f;

    // Internal state
    private float _phaseIncrement;
    private float _currentPhase;
    private readonly Random _random = new();

    /// <inheritdoc/>
    public override string Name { get; set; } = "Oscillator";

    /// <inheritdoc/>
    protected override void GenerateAudio(Span<float> buffer, int channels)
    {
        // Calculate the phase increment per sample based on the frequency
        _phaseIncrement = (float)(2.0 * Math.PI * Frequency / Format.SampleRate);

        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = GenerateSample();
        }
    }

    /// <summary>
    /// Generates a single audio sample based on the current waveform type, phase, and amplitude.
    /// This method updates the internal phase for the next sample.
    /// </summary>
    /// <returns>The generated audio sample value.</returns>
    private float GenerateSample()
    {
        var sampleValue = Type switch
        {
            WaveformType.Sine => MathF.Sin(_currentPhase + Phase),
            WaveformType.Square => _currentPhase + Phase < Math.PI ? 1f : -1f,
            WaveformType.Sawtooth => (float)(2.0 * (_currentPhase + Phase) / (2.0 * Math.PI) - 1.0),
            WaveformType.Triangle =>
                (float)(2.0 * Math.Abs(2.0 * (_currentPhase + Phase) / (2.0 * Math.PI) - 1.0) - 1.0),
            WaveformType.Noise => (float)(_random.NextDouble() * 2.0 - 1.0),
            WaveformType.Pulse =>
                _currentPhase + Phase < Math.PI * PulseWidth ? 1f : -1f,
            _ => 0f
        };


        // Update the phase for the next sample
        _currentPhase += _phaseIncrement;
        if (_currentPhase >= 2.0 * Math.PI)
        {
            _currentPhase -= (float)(2.0 * Math.PI);
        }

        return sampleValue * Amplitude;
    }
}
