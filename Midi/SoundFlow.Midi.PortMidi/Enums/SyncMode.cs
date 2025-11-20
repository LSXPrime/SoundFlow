namespace SoundFlow.Midi.PortMidi.Enums;

/// <summary>
/// Defines the MIDI synchronization mode for the audio engine.
/// </summary>
public enum SyncMode
{
    /// <summary>
    /// Synchronization is disabled. The composition's transport is controlled internally.
    /// </summary>
    Off,

    /// <summary>
    /// The engine acts as a master, sending MIDI Clock and transport messages to an output device.
    /// </summary>
    Master,

    /// <summary>
    /// The engine acts as a slave, receiving MIDI Clock, MTC, and transport messages from an input device.
    /// </summary>
    Slave
}