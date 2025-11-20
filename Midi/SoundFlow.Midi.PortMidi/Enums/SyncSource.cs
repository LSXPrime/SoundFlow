namespace SoundFlow.Midi.PortMidi.Enums;

/// <summary>
/// Defines the source of synchronization when the engine is in Slave mode.
/// </summary>
public enum SyncSource
{
    /// <summary>
    /// The transport is controlled by the internal application clock.
    /// </summary>
    Internal,

    /// <summary>
    /// The transport is synchronized to incoming MIDI Clock messages.
    /// </summary>
    MidiClock,

    /// <summary>
    /// The transport is synchronized to incoming MIDI Time Code (MTC) messages.
    /// </summary>
    Mtc
}