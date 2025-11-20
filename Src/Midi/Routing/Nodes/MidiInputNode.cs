using SoundFlow.Midi.Devices;
using SoundFlow.Midi.Interfaces;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;

namespace SoundFlow.Midi.Routing.Nodes;

/// <summary>
/// An internal implementation of <see cref="IMidiSourceNode"/> that represents a physical MIDI input device.
/// </summary>
public sealed class MidiInputNode : IMidiSourceNode
{
    /// <summary>
    /// Gets the underlying physical device instance.
    /// </summary>
    public MidiInputDevice Device { get; }

    /// <inheritdoc />
    public string Name => Device.Info.Name;

    /// <inheritdoc />
    public event Action<MidiMessage>? OnMessageOutput;

    /// <inheritdoc />
    public event Action<byte[]>? OnSysExOutput;

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiInputNode"/> class.
    /// </summary>
    /// <param name="device">The physical MIDI input device this node represents.</param>
    public MidiInputNode(MidiInputDevice device)
    {
        Device = device;
    }

    /// <summary>
    /// Triggers the <see cref="OnMessageOutput"/> event. Called by the MidiManager.
    /// </summary>
    /// <param name="message">The message to output.</param>
    public void TriggerMessageOutput(MidiMessage message)
    {
        OnMessageOutput?.Invoke(message);
    }
    
    /// <summary>
    /// Triggers the <see cref="OnSysExOutput"/> event. Called by the MidiManager.
    /// </summary>
    /// <param name="data">The SysEx data payload to output.</param>
    public void TriggerSysExOutput(byte[] data)
    {
        OnSysExOutput?.Invoke(data);
    }
}