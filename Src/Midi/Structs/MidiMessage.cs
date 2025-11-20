using SoundFlow.Midi.Enums;

namespace SoundFlow.Midi.Structs;

/// <summary>
/// Represents a standard MIDI channel message.
/// </summary>
public readonly record struct MidiMessage
{
    /// <summary>
    /// Gets the timestamp of the message, typically provided by the MIDI backend.
    /// </summary>
    public long Timestamp { get; }

    /// <summary>
    /// Gets the raw status byte of the MIDI message.
    /// </summary>
    public byte StatusByte { get; init; }

    /// <summary>
    /// Gets the first data byte of the MIDI message.
    /// </summary>
    public byte Data1 { get; }

    /// <summary>
    /// Gets the second data byte of the MIDI message.
    /// </summary>
    public byte Data2 { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiMessage"/> struct.
    /// </summary>
    /// <param name="status">The status byte.</param>
    /// <param name="data1">The first data byte.</param>
    /// <param name="data2">The second data byte.</param>
    /// <param name="timestamp">The message timestamp.</param>
    public MidiMessage(byte status, byte data1, byte data2, long timestamp = 0)
    {
        StatusByte = status;
        Data1 = data1;
        Data2 = data2;
        Timestamp = timestamp;
    }

    /// <summary>
    /// Gets the MIDI command type from the status byte.
    /// </summary>
    public MidiCommand Command => (MidiCommand)(StatusByte & 0xF0);

    /// <summary>
    /// Gets the MIDI channel (1-16) from the status byte.
    /// </summary>
    public int Channel => (StatusByte & 0x0F) + 1;

    /// <summary>
    /// Gets the note number for Note On/Off or Polyphonic Key Pressure messages.
    /// </summary>
    public int NoteNumber => Data1;

    /// <summary>
    /// Gets the velocity for Note On/Off messages.
    /// </summary>
    public int Velocity => Data2;

    /// <summary>
    /// Gets the pressure value for Polyphonic Key Pressure or Channel Pressure messages.
    /// </summary>
    public int Pressure => Data2;

    /// <summary>
    /// Gets the controller number for Control Change messages.
    /// </summary>
    public int ControllerNumber => Data1;

    /// <summary>
    /// Gets the controller value for Control Change messages.
    /// </summary>
    public int ControllerValue => Data2;
    
    /// <summary>
    /// Gets the pitch bend value, combining Data1 and Data2 into a 14-bit value centered at 8192.
    /// </summary>
    public int PitchBendValue => (Data2 << 7) | Data1;
}