namespace SoundFlow.Interfaces;

/// <summary>
/// Defines an interface for objects that can be a target for MIDI mapping.
/// It ensures that any mappable object has a stable, unique identifier.
/// </summary>
public interface IMidiMappable
{
    /// <summary>
    /// Gets the unique identifier for this mappable instance.
    /// This ID is used to persist and restore MIDI mappings.
    /// </summary>
    Guid Id { get; }
}