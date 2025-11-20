using SoundFlow.Midi.Devices;
using SoundFlow.Midi.Interfaces;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;

namespace SoundFlow.Midi.Routing.Nodes;

/// <summary>
/// An internal implementation of <see cref="IMidiDestinationNode"/> that represents a physical MIDI output device.
/// </summary>
public sealed class MidiOutputNode : IMidiDestinationNode
{
    /// <summary>
    /// Gets the underlying physical device instance.
    /// </summary>
    public MidiOutputDevice Device { get; }
    
    /// <inheritdoc />
    public string Name => Device.Info.Name;

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiOutputNode"/> class.
    /// </summary>
    /// <param name="device">The physical MIDI output device this node represents.</param>
    public MidiOutputNode(MidiOutputDevice device)
    {
        Device = device;
    }
    
    /// <inheritdoc />
    public Result ProcessMessage(MidiMessage message)
    {
        return !Device.IsDisposed ? Device.SendMessage(message) : Result.Fail(new ObjectDisposedError(nameof(MidiOutputNode)));
    }

    /// <inheritdoc />
    public void ProcessMidiMessage(MidiMessage message)
    {
        ProcessMessage(message);
    }
}