using SoundFlow.Midi.Abstracts;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;

namespace SoundFlow.Midi.Devices;

/// <summary>
/// Represents a MIDI output device capable of sending MIDI messages.
/// </summary>
public abstract class MidiOutputDevice : MidiDevice
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MidiOutputDevice"/> class.
    /// </summary>
    /// <param name="info">The device information.</param>
    protected MidiOutputDevice(MidiDeviceInfo info) : base(info) { }

    /// <summary>
    /// Sends a MIDI channel message to the output device.
    /// </summary>
    /// <param name="message">The MIDI message to send.</param>
    public abstract Result SendMessage(MidiMessage message);

    /// <summary>
    /// Sends a System Exclusive (SysEx) message to the output device.
    /// </summary>
    /// <param name="data">The raw SysEx data payload, excluding the start (F0) and end (F7) bytes.</param>
    public abstract Result SendSysEx(byte[] data);
}