namespace SoundFlow.Midi.Enums;

/// <summary>
/// Represents the command portion of a MIDI status byte, defining the type of MIDI message.
/// </summary>
public enum MidiCommand : byte
{
    /// <summary>
    /// Note Off message (e.g., releasing a key).
    /// </summary>
    NoteOff = 0x80,

    /// <summary>
    /// Note On message (e.g., pressing a key). A Note On with velocity 0 is often interpreted as a Note Off.
    /// </summary>
    NoteOn = 0x90,

    /// <summary>
    /// Polyphonic Key Pressure (Aftertouch) message.
    /// </summary>
    PolyphonicKeyPressure = 0xA0,

    /// <summary>
    /// Control Change (CC) message, used for adjusting parameters.
    /// </summary>
    ControlChange = 0xB0,

    /// <summary>
    /// Program Change message, used for changing instruments or presets.
    /// </summary>
    ProgramChange = 0xC0,

    /// <summary>
    /// Channel Pressure (Aftertouch) message.
    /// </summary>
    ChannelPressure = 0xD0,

    /// <summary>
    /// Pitch Bend message.
    /// </summary>
    PitchBend = 0xE0,

    /// <summary>
    /// System Exclusive (SysEx) message start.
    /// </summary>
    SystemExclusive = 0xF0
}