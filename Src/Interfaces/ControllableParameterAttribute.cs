namespace SoundFlow.Interfaces;

/// <summary>
/// Defines the scaling behavior for a controllable parameter when mapped to a normalized input (e.g., 0.0 to 1.0).
/// </summary>
public enum MappingScale
{
    /// <summary>
    /// The parameter responds linearly to the input. Suitable for parameters like volume, pan, or wet/dry mix.
    /// </summary>
    Linear,

    /// <summary>
    /// The parameter responds logarithmically to the input. This provides finer control at the lower end of the range
    /// and is ideal for frequency-based parameters like filter cutoff to match human perception.
    /// </summary>
    Logarithmic
}

/// <summary>
/// An attribute used to decorate properties on `IMidiMappable` objects, exposing them for real-time control
/// and providing metadata for user interfaces and the mapping system.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ControllableParameterAttribute : Attribute
{
    /// <summary>
    /// Gets the user-friendly display name for the parameter.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the minimum valid value for the parameter.
    /// </summary>
    public double MinValue { get; }

    /// <summary>
    /// Gets the maximum valid value for the parameter.
    /// </summary>
    public double MaxValue { get; }

    /// <summary>
    /// Gets the scaling curve that should be used when mapping a normalized value (0.0 to 1.0) to this parameter.
    /// </summary>
    public MappingScale Scale { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ControllableParameterAttribute"/> class.
    /// </summary>
    /// <param name="displayName">The user-friendly display name for the parameter.</param>
    /// <param name="minValue">The minimum valid value for the parameter.</param>
    /// <param name="maxValue">The maximum valid value for the parameter.</param>
    /// <param name="scale">The scaling curve to use for mapping. Defaults to Linear.</param>
    public ControllableParameterAttribute(string displayName, double minValue, double maxValue, MappingScale scale = MappingScale.Linear)
    {
        DisplayName = displayName;
        MinValue = minValue;
        MaxValue = maxValue;
        Scale = scale;
    }
}