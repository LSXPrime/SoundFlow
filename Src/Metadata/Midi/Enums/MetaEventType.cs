namespace SoundFlow.Metadata.Midi.Enums;

/// <summary>
/// Defines the types for Standard MIDI File meta-events.
/// </summary>
public enum MetaEventType : byte
{
    /// <summary>
    /// Sequence Number event.
    /// </summary>
    SequenceNumber = 0x00,

    /// <summary>
    /// Text event.
    /// </summary>
    Text = 0x01,

    /// <summary>
    /// Copyright Notice event.
    /// </summary>
    CopyrightNotice = 0x02,

    /// <summary>
    /// Sequence/Track Name event.
    /// </summary>
    TrackName = 0x03,

    /// <summary>
    /// Instrument Name event.
    /// </summary>
    InstrumentName = 0x04,

    /// <summary>
    /// Lyric event.
    /// </summary>
    Lyric = 0x05,

    /// <summary>
    /// Marker event.
    /// </summary>
    Marker = 0x06,

    /// <summary>
    /// Cue Point event.
    /// </summary>
    CuePoint = 0x07,

    /// <summary>
    /// MIDI Channel Prefix event.
    /// </summary>
    MidiChannelPrefix = 0x20,

    /// <summary>
    /// End of Track event.
    /// </summary>
    EndOfTrack = 0x2F,

    /// <summary>
    /// Set Tempo event.
    /// </summary>
    SetTempo = 0x51,

    /// <summary>
    /// SMPTE Offset event.
    /// </summary>
    SmpteOffset = 0x54,

    /// <summary>
    /// Time Signature event.
    /// </summary>
    TimeSignature = 0x58,

    /// <summary>
    /// Key Signature event.
    /// </summary>
    KeySignature = 0x59,

    /// <summary>
    /// Sequencer-Specific Meta-Event.
    /// </summary>
    SequencerSpecific = 0x7F
}