using SoundFlow.Abstracts;
using SoundFlow.Midi.Interfaces;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;

namespace SoundFlow.Midi.Routing.Nodes;

/// <summary>
/// An internal implementation of <see cref="IMidiDestinationNode"/> that wraps an <see cref="IMidiControllable"/> component.
/// </summary>
public sealed class MidiTargetNode : IMidiDestinationNode
{
    /// <summary>
    /// The internal component to send MIDI messages to.
    /// </summary>
    public readonly IMidiControllable? Target;

    /// <inheritdoc />
    public string Name => Target is SoundComponent sc ? sc.Name : Target?.GetType().Name ?? "Unknown";

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiTargetNode"/> class.
    /// </summary>
    /// <param name="target">The internal component to send MIDI messages to.</param>
    public MidiTargetNode(IMidiControllable? target)
    {
        Target = target;
    }

    /// <inheritdoc />
    public Result ProcessMessage(MidiMessage message)
    {
        // NOTE: Adding a try-catch here to handle failures might be performance-intensive in real-time,
        // since it's calling `ProcessMidiMessage` which exist in almost every single component.
        Target?.ProcessMidiMessage(message);
        return Result.Ok();
    }
    
    /// <inheritdoc />
    public void ProcessMidiMessage(MidiMessage message)
    {
        ProcessMessage(message);
    }
}