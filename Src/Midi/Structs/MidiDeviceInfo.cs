namespace SoundFlow.Midi.Structs;

/// <summary>
/// Represents informational details for a MIDI device.
/// </summary>
public readonly record struct MidiDeviceInfo
{
    /// <summary>
    /// The unique integer identifier for the device, specific to the MIDI backend.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// The human-readable name of the device.
    /// </summary>
    public string Name { get; init; }
}