using SoundFlow.Editing;
using SoundFlow.Editing.Mapping;

namespace SoundFlow.Editing.Persistence;

/// <summary>
/// Represents the serializable data for a single MIDI mapping, suitable for project files.
/// </summary>
public class ProjectMidiMapping
{
    /// <summary>
    /// Gets or sets the unique name of the source MIDI input device.
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MIDI channel (0 for Omni).
    /// </summary>
    public int Channel { get; set; }

    /// <summary>
    /// Gets or sets the type of MIDI message.
    /// </summary>
    public MidiMappingSourceType MessageType { get; set; }

    /// <summary>
    /// Gets or sets the message-specific parameter (e.g., CC number).
    /// </summary>
    public int MessageParameter { get; set; }
    
    /// <summary>
    /// Gets or sets the unique ID of the target object instance.
    /// </summary>
    public Guid TargetObjectId { get; set; }

    /// <summary>
    /// Gets or sets the type of member being targeted (Property or Method).
    /// </summary>
    public MidiMappingTargetType TargetType { get; set; }

    /// <summary>
    /// Gets or sets the name of the property or method to be controlled.
    /// </summary>
    public string TargetMemberName { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the behavior of the mapping (e.g., Absolute, Toggle).
    /// </summary>
    public MidiMappingBehavior Behavior { get; set; }

    /// <summary>
    /// Gets or sets the activation threshold for Toggle or Trigger behaviors.
    /// </summary>
    public int ActivationThreshold { get; set; } = 1;

    /// <summary>
    /// Gets or sets the list of arguments for a method call. Only used if TargetType is Method.
    /// </summary>
    public List<MethodArgument> MethodArguments { get; set; } = [];

    /// <summary>
    /// Gets or sets the primary value transformer for the mapping.
    /// </summary>
    public ValueTransformer Transformer { get; set; } = new();
}