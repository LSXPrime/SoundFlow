using SoundFlow.Components;
using SoundFlow.Interfaces;
using SoundFlow.Metadata.SoundFont;
using SoundFlow.Structs;
using SoundFlow.Synthesis.Interfaces;
using SoundFlow.Synthesis.Voices;

namespace SoundFlow.Synthesis.Instruments;

/// <summary>
/// A factory class that defines how to construct a synthesizer voice.
/// It encapsulates the generator chain and parameters for a specific sound.
/// </summary>
public class VoiceDefinition
{
    internal readonly AudioFormat Format;

    // Parameters for the voice
    internal readonly Oscillator.WaveformType OscillatorType;
    internal readonly int Unison;
    internal readonly float Detune;
    internal readonly float AttackTime;
    internal readonly float DecayTime;
    internal readonly float SustainLevel;
    internal readonly float ReleaseTime;
    internal readonly bool UseFilter;

    // Sample-based parameters
    internal SampleData? Sample;

    /// <summary>
    /// Initializes a new instance of the <see cref="VoiceDefinition"/> class.
    /// </summary>
    /// <param name="format">The audio format.</param>
    /// <param name="oscType">The waveform for the oscillator.</param>
    /// <param name="unison">The number of unison voices (oscillators) per note.</param>
    /// <param name="detune">The maximum detune amount for unison voices (e.g., 0.005 for 0.5%).</param>
    /// <param name="attack">Attack time in seconds.</param>
    /// <param name="decay">Decay time in seconds.</param>
    /// <param name="sustain">Sustain level (0-1).</param>
    /// <param name="release">Release time in seconds.</param>
    /// <param name="useFilter">Whether to apply a modulated low-pass filter to this voice.</param>
    public VoiceDefinition(
        AudioFormat format, 
        Oscillator.WaveformType oscType, 
        int unison, 
        float detune,
        float attack, 
        float decay, 
        float sustain, 
        float release,
        bool useFilter = false)
    {
        Format = format;
        OscillatorType = oscType;
        Unison = Math.Max(1, unison);
        Detune = detune;
        AttackTime = attack;
        DecayTime = decay;
        SustainLevel = sustain;
        ReleaseTime = release;
        UseFilter = useFilter;
    }

    /// <summary>
    /// Creates a new <see cref="IVoice"/> instance based on this definition.
    /// </summary>
    /// <param name="context">The context for the new voice.</param>
    /// <returns>A new, fully constructed IVoice.</returns>
    public IVoice CreateVoice(VoiceContext context)
    {
        // Prioritize sample-based generation if a sample is defined.
        if (Sample != null)
        {
            return new SampleVoice(this, context);
        }
        return new Voice(this, context);
    }
}