namespace SoundFlow.Synthesis.Voices;

/// <summary>
/// Provides contextual information for a single voice's generation process.
/// This class is passed down through a voice's IGenerator chain.
/// </summary>
public record VoiceContext
{
    /// <summary>
    /// The MIDI note number being played.
    /// </summary>
    public int NoteNumber;

    /// <summary>
    /// The velocity of the note press (0-127).
    /// </summary>
    public int Velocity;

    /// <summary>
    /// The final, calculated frequency for the current processing step, including all modulations.
    /// This is the value that IGenerators like oscillators should use.
    /// </summary>
    public float Frequency;

    /// <summary>
    /// The pure, unmodified frequency of the MIDI note number before any pitch bend or detuning.
    /// </summary>
    public float BaseFrequency;

    /// <summary>
    /// The global pitch bend value for the entire MIDI channel, in semitones.
    /// </summary>
    public float ChannelPitchBend;

    /// <summary>
    /// The sample rate of the audio engine.
    /// </summary>
    public int SampleRate;
}