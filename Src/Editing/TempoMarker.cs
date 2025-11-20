namespace SoundFlow.Editing;

/// <summary>
/// Represents a tempo change at a specific point in the composition's timeline.
/// </summary>
/// <param name="Time">The absolute time on the composition timeline where the tempo change occurs.</param>
/// <param name="BeatsPerMinute">The new tempo in beats per minute.</param>
public readonly record struct TempoMarker(TimeSpan Time, double BeatsPerMinute);