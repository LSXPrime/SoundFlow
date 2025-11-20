namespace SoundFlow.Editing.Mapping;

/// <summary>
/// Defines the type of MIDI message that can be used as a mapping source.
/// </summary>
public enum MidiMappingSourceType
{
    /// <summary>
    /// A standard MIDI Control Change message (CC 0-127).
    /// </summary>
    ControlChange,

    /// <summary>
    /// A MIDI Note On message, typically used for triggers or velocity-sensitive controls.
    /// </summary>
    NoteOn,

    /// <summary>
    /// A MIDI Note Off message, typically used to detect the release of a key/pad.
    /// </summary>
    NoteOff,

    /// <summary>
    /// A 14-bit Pitch Bend message.
    /// </summary>
    PitchBend,

    /// <summary>
    /// A high-resolution 14-bit Control Change message, typically implemented using NRPN or RPN.
    /// </summary>
    HighResolutionControlChange
}

/// <summary>
/// Defines the behavior of the MIDI mapping when triggered.
/// </summary>
public enum MidiMappingBehavior
{
    /// <summary>
    /// The transformed MIDI value directly sets the property value.
    /// </summary>
    Absolute,

    /// <summary>
    /// Each trigger flips the current value of a boolean property.
    /// </summary>
    Toggle,

    /// <summary>
    /// The mapping triggers a method call.
    /// </summary>
    Trigger,

    /// <summary>
    /// The incoming MIDI value increments or decrements the current property value.
    /// </summary>
    Relative
}

/// <summary>
/// Defines the target type for a MIDI mapping.
/// </summary>
public enum MidiMappingTargetType
{
    /// <summary>
    /// The mapping targets a public property on the object.
    /// </summary>
    Property,

    /// <summary>
    /// The mapping targets a public method on the object.
    /// </summary>
    Method
}

/// <summary>
/// Defines the source of a value for a method argument in a Trigger mapping.
/// </summary>
public enum MidiMappingArgumentSource
{
    /// <summary>
    /// Use the incoming MIDI value (e.g., velocity, CC value).
    /// </summary>
    MidiValue,

    /// <summary>
    /// Use a fixed, constant value defined in the mapping.
    /// </summary>
    Constant
}

/// <summary>
/// Defines the transformation curve for mapping a MIDI value to a parameter value.
/// </summary>
public enum MidiMappingCurveType
{
    /// <summary>
    /// A straight, 1:1 scaling between the source and target ranges.
    /// </summary>
    Linear,

    /// <summary>
    /// A curve where small changes in the source result in larger changes in the target at the higher end of the range.
    /// </summary>
    Exponential,

    /// <summary>
    /// A curve where small changes in the source result in larger changes in the target at the lower end of the range.
    /// </summary>
    Logarithmic
}

/// <summary>
/// Defines the source of a MIDI mapping, specifying which message will trigger it.
/// </summary>
public record MidiInputSource
{
    /// <summary>
    /// The unique name of the MIDI input device.
    /// </summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>
    /// The MIDI channel (1-16). A value of 0 represents Omni (any channel).
    /// </summary>
    public int Channel { get; init; }

    /// <summary>
    /// The type of MIDI message to listen for.
    /// </summary>
    public MidiMappingSourceType MessageType { get; init; }

    /// <summary>
    /// The specific parameter of the message.
    /// For CC, this is the controller number (0-127).
    /// For Notes, this is the note number (0-127).
    /// For High-Resolution CC, this is the 14-bit NRPN/RPN number.
    /// For Pitch Bend, this value is ignored.
    /// </summary>
    public int MessageParameter { get; init; }
}

/// <summary>
/// Defines a single argument for a method call in a Trigger mapping.
/// </summary>
public record MethodArgument
{
    /// <summary>
    /// The source of the value for this argument.
    /// </summary>
    public MidiMappingArgumentSource Source { get; init; } = MidiMappingArgumentSource.MidiValue;

    /// <summary>
    /// The constant value to use if the Source is Constant.
    /// This should be a JSON-serializable type (string, number, bool).
    /// </summary>
    public object? ConstantValue { get; init; }

    /// <summary>
    /// An optional transformer to apply if the Source is MidiValue.
    /// If null, the raw MIDI value is used.
    /// </summary>
    public ValueTransformer? Transformer { get; init; }
}

/// <summary>
/// Defines the target of a MIDI mapping, specifying which property or method will be controlled.
/// </summary>
public record MidiMappingTarget
{
    /// <summary>
    /// The unique identifier of the target object instance (e.g., a specific Filter).
    /// </summary>
    public Guid TargetObjectId { get; init; }

    /// <summary>
    /// The type of member being targeted (Property or Method).
    /// </summary>
    public MidiMappingTargetType TargetType { get; init; } = MidiMappingTargetType.Property;

    /// <summary>
    /// The name of the public property or method on the target object to be modified or invoked.
    /// </summary>
    public string TargetMemberName { get; init; } = string.Empty;

    /// <summary>
    /// A list of arguments to be passed if the target is a method. Ignored for properties.
    /// </summary>
    public List<MethodArgument> MethodArguments { get; init; } = [];
}

/// <summary>
/// Defines the transformation logic for converting an incoming MIDI value
/// to the range expected by the target property.
/// </summary>
public record ValueTransformer
{
    /// <summary>
    /// The minimum value of the MIDI input range (e.g., 0 for CC).
    /// </summary>
    public float SourceMin { get; init; }

    /// <summary>
    /// The maximum value of the MIDI input range (e.g., 127 for CC, 16383 for 14-bit).
    /// </summary>
    public float SourceMax { get; init; } = 127;

    /// <summary>
    /// The minimum value of the target property's range.
    /// </summary>
    public float TargetMin { get; init; }

    /// <summary>
    /// The maximum value of the target property's range.
    /// </summary>
    public float TargetMax { get; init; } = 1.0f;

    /// <summary>
    /// The response curve for the value transformation.
    /// </summary>
    public MidiMappingCurveType CurveType { get; init; } = MidiMappingCurveType.Linear;
}

/// <summary>
/// Represents a complete, configurable link between a specific MIDI message
/// and a controllable parameter within the composition.
/// </summary>
public sealed class MidiMapping
{
    /// <summary>
    /// Gets the unique identifier for this mapping instance.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the MIDI message source that triggers this mapping.
    /// </summary>
    public MidiInputSource Source { get; set; }

    /// <summary>
    /// Gets or sets the parameter that this mapping controls.
    /// </summary>
    public MidiMappingTarget Target { get; set; }

    /// <summary>
    /// Gets or sets the value transformer that defines how the input value is scaled.
    /// This is primarily used for Absolute and Relative behaviors.
    /// </summary>
    public ValueTransformer Transformer { get; set; }

    /// <summary>
    /// Gets or sets the behavior of the mapping (e.g., Absolute, Toggle).
    /// </summary>
    public MidiMappingBehavior Behavior { get; set; }

    /// <summary>
    /// Gets an optional activation threshold. For Toggle or Trigger behaviors,
    /// the action only occurs if the incoming MIDI value is at or above this threshold.
    /// </summary>
    public int ActivationThreshold { get; set; } = 1;

    /// <summary>
    /// Gets a value indicating whether this mapping is currently resolved to a valid, live target.
    /// Mappings can become unresolved if a project is loaded and the target object or member is no longer found.
    /// </summary>
    public bool IsResolved { get; internal set; } = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiMapping"/> class.
    /// </summary>
    /// <param name="source">The MIDI message source.</param>
    /// <param name="target">The target parameter.</param>
    /// <param name="transformer">The value transformation logic.</param>
    /// <param name="behavior">The behavior of the mapping.</param>
    public MidiMapping(MidiInputSource source, MidiMappingTarget target, ValueTransformer transformer,
        MidiMappingBehavior behavior = MidiMappingBehavior.Absolute)
    {
        Source = source;
        Target = target;
        Transformer = transformer;
        Behavior = behavior;
    }
}