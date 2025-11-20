using SoundFlow.Components;
using SoundFlow.Interfaces;
using SoundFlow.Structs;
using SoundFlow.Synthesis.Instruments;
using SoundFlow.Synthesis.Interfaces;

namespace SoundFlow.Synthesis.Banks;

/// <summary>
/// A simple, hardcoded implementation of an instrument bank for demonstration and testing.
/// </summary>
public sealed class BasicInstrumentBank : IInstrumentBank
{
    private readonly Dictionary<(int bank, int program), Instrument> _instruments = new();
    private readonly Instrument _fallbackInstrument;

    /// <summary>
    /// Initializes a new instance of the <see cref="BasicInstrumentBank"/> class.
    /// </summary>
    /// <param name="format">The audio format required to create voice definitions.</param>
    public BasicInstrumentBank(AudioFormat format)
    {
        // Internal Fallback Instrument for this bank
        var fallbackVoiceDef = new VoiceDefinition(format, Oscillator.WaveformType.Sine, 1, 0, 0.01f, 0.1f, 0.0f, 0.2f);
        _fallbackInstrument = new Instrument([], fallbackVoiceDef, isFallback: true);

        // Instrument 1: Program 0 (A Velocity-Sensitive Electric Piano)
        // Soft layer for low velocities
        var softEpianoVoice = new VoiceDefinition(format, Oscillator.WaveformType.Sine, 2, 0.003f, 0.02f, 1.5f, 0.2f, 0.8f);
        // Bright layer for high velocities
        var hardEpianoVoice = new VoiceDefinition(format, Oscillator.WaveformType.Sawtooth, 3, 0.004f, 0.01f, 1.0f, 0.4f, 0.6f, useFilter: true);

        var epianoMappings = new List<VoiceMapping>
        {
            new(softEpianoVoice) { MinVelocity = 0, MaxVelocity = 64 },
            new(hardEpianoVoice) { MinVelocity = 65, MaxVelocity = 127 }
        };
        // Create as a non-fallback instrument
        _instruments[(0, 0)] = new Instrument(epianoMappings, fallbackVoiceDef);
        

        // Instrument 2: Program 10 (GM #11 Music Box)
        var musicBoxVoice = new VoiceDefinition(
            format,
            oscType: Oscillator.WaveformType.Sine,
            unison: 2,
            detune: 0.002f,
            attack: 0.001f,
            decay: 1.5f,
            sustain: 0.0f,
            release: 0.3f
        );
        var musicBoxMappings = new List<VoiceMapping> { new(musicBoxVoice) };
        _instruments[(0, 10)] = new Instrument(musicBoxMappings, fallbackVoiceDef);


        // Instrument 3: Program 80 (GM #81 Lead 1 "Supersaw")
        var supersawLeadVoice = new VoiceDefinition(
            format,
            oscType: Oscillator.WaveformType.Sawtooth,
            unison: 7,
            detune: 0.006f,
            attack: 0.05f,
            decay: 0.3f,
            sustain: 0.7f,
            release: 0.8f,
            useFilter: true
        );
        var supersawMappings = new List<VoiceMapping> { new(supersawLeadVoice) };
        _instruments[(0, 80)] = new Instrument(supersawMappings, fallbackVoiceDef);
    }

    /// <inheritdoc />
    public Instrument GetInstrument(int bank, int program)
    {
        return _instruments.GetValueOrDefault((bank, program), _fallbackInstrument);
    }
}