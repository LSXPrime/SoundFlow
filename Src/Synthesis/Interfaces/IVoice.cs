using SoundFlow.Midi.Structs;

namespace SoundFlow.Synthesis.Interfaces;

/// <summary>
/// Represents a single, active sound-producing voice within a synthesizer.
/// A voice is typically triggered by a Note On event and ends after its release phase.
/// </summary>
public interface IVoice
{
    /// <summary>
    /// Gets the MIDI note number this voice is playing.
    /// </summary>
    int NoteNumber { get; }

    /// <summary>
    /// Gets the velocity at which this voice was triggered.
    /// </summary>
    int Velocity { get; }
    
    /// <summary>
    /// Gets a value indicating if the voice is currently in its release phase.
    /// </summary>
    bool IsReleasing { get; }

    /// <summary>
    /// Gets or sets a value indicating if this voice is being held by the sustain pedal.
    /// </summary>
    bool IsSustained { get; set; }

    /// <summary>
    /// Gets a value indicating whether the voice has finished its lifecycle (e.g., completed its release envelope)
    /// and can be removed from the active voice pool.
    /// </summary>
    bool IsFinished { get; }

    /// <summary>
    /// Renders the voice's audio output into the provided buffer.
    /// </summary>
    /// <param name="buffer">The buffer to fill with audio data. The voice should add its output to this buffer.</param>
    void Render(Span<float> buffer);

    /// <summary>
    /// Triggers the release phase of the voice (e.g., when a Note Off message is received).
    /// </summary>
    void NoteOff();

    /// <summary>
    /// Immediately silences the voice, typically with a very short fade-out to prevent clicks.
    /// Used for voice stealing or "all notes off" commands.
    /// </summary>
    void Kill();

    /// <summary>
    /// Processes a MIDI control message to update the voice's parameters in real-time
    /// (e.g., pitch bend, modulation). This method is primarily for global channel controls.
    /// </summary>
    /// <param name="message">The MIDI message to process.</param>
    /// <param name="channelPitchBend">The current pitch bend of the channel in semitones.</param>
    void ProcessMidiControl(MidiMessage message, float channelPitchBend);

    /// <summary>
    /// Updates the per-note pitch bend for this voice (MPE).
    /// </summary>
    /// <param name="semitones">The pitch bend amount in semitones.</param>
    void SetPerNotePitchBend(float semitones);

    /// <summary>
    /// Updates the per-note pressure for this voice (MPE).
    /// </summary>
    /// <param name="value">The pressure value, normalized from 0.0 to 1.0.</param>
    void SetPerNotePressure(float value);

    /// <summary>
    /// Updates the per-note timbre/CC74 for this voice (MPE).
    /// </summary>
    /// <param name="value">The timbre value, normalized from 0.0 to 1.0.</param>
    void SetPerNoteTimbre(float value);
}