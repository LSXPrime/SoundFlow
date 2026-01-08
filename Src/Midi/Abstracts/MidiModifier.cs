using System.Diagnostics.CodeAnalysis;
using SoundFlow.Interfaces;
using SoundFlow.Midi.Structs;

namespace SoundFlow.Midi.Abstracts;

/// <summary>
/// Base class for real-time MIDI processing components (MIDI effects).
/// Implementations of this class can filter, transform, or generate MIDI messages.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)]
public abstract class MidiModifier : IMidiMappable
{
    /// <inheritdoc />
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// The name of the MIDI modifier.
    /// </summary>
    public virtual string Name { get; set; } = "MIDI Modifier";

    /// <summary>
    /// Whether the modifier is enabled or not.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Processes an incoming MIDI message and returns a collection of zero, one, or more resulting messages.
    /// </summary>
    /// <param name="message">The input MIDI message to process.</param>
    /// <returns>
    /// An enumerable collection of MIDI messages.
    /// - Return an empty collection to filter (drop) the message.
    /// - Return a collection with one modified message to transform it.
    /// - Return a collection with multiple messages to generate new events (e.g., for an arpeggiator).
    /// </returns>
    public abstract IEnumerable<MidiMessage> Process(MidiMessage message);
}