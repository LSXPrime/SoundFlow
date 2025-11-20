using SoundFlow.Midi.Abstracts;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;

namespace SoundFlow.Midi.Devices;

/// <summary>
/// Delegate for handling received MIDI channel messages.
/// </summary>
/// <param name="message">The received MIDI message.</param>
/// <param name="sourceDeviceInfo">The informational struct for the device that sent the message.</param>
public delegate void MidiMessageReceivedCallback(MidiMessage message, MidiDeviceInfo sourceDeviceInfo);

/// <summary>
/// Delegate for handling received MIDI System Exclusive (SysEx) messages.
/// </summary>
/// <param name="data">The raw SysEx data payload, excluding the start (F0) and end (F7) bytes.</param>
/// <param name="sourceDeviceInfo">The informational struct for the device that sent the message.</param>
public delegate void SysExMessageReceivedCallback(byte[] data, MidiDeviceInfo sourceDeviceInfo);

/// <summary>
/// Represents a MIDI input device capable of receiving MIDI messages.
/// </summary>
public abstract class MidiInputDevice : MidiDevice
{
    /// <summary>
    /// Occurs when a new MIDI channel message is received from this device.
    /// </summary>
    public event MidiMessageReceivedCallback? OnMessageReceived;

    /// <summary>
    /// Occurs when a new System Exclusive (SysEx) message is received from this device.
    /// </summary>
    public event SysExMessageReceivedCallback? OnSysExReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiInputDevice"/> class.
    /// </summary>
    /// <param name="info">The device information.</param>
    protected MidiInputDevice(MidiDeviceInfo info) : base(info) { }

    /// <summary>
    /// Invokes the <see cref="OnMessageReceived"/> event with the received message.
    /// This method is intended to be called by the backend implementation.
    /// </summary>
    /// <param name="message">The MIDI message received from the device.</param>
    protected virtual void InvokeOnMessageReceived(MidiMessage message)
    {
        OnMessageReceived?.Invoke(message, Info);
    }

    /// <summary>
    /// Invokes the <see cref="OnSysExReceived"/> event with the received SysEx data.
    /// This method is intended to be called by the backend implementation.
    /// </summary>
    /// <param name="data">The raw SysEx data payload received from the device.</param>
    protected virtual void InvokeOnSysExReceived(byte[] data)
    {
        OnSysExReceived?.Invoke(data, Info);
    }
}