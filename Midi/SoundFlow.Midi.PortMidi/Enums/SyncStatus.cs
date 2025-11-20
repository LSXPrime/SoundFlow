namespace SoundFlow.Midi.PortMidi.Enums;

/// <summary>
/// Represents the current lock status of the MIDI synchronization.
/// </summary>
public enum SyncStatus
{
    /// <summary>
    /// The engine is not synchronized to an external source.
    /// </summary>
    Unlocked,

    /// <summary>
    /// The engine is successfully synchronized and locked to the external source.
    /// </summary>
    Locked
}