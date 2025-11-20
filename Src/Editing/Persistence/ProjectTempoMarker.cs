namespace SoundFlow.Editing.Persistence;

/// <summary>
/// Represents a serializable tempo marker for saving and loading composition projects.
/// </summary>
public class ProjectTempoMarker
{
    /// <summary>
    /// Gets or sets the time of the tempo marker.
    /// </summary>
    public TimeSpan Time { get; set; }
    
    /// <summary>
    /// Gets or sets the tempo in beats per minute.
    /// </summary>
    public double BeatsPerMinute { get; set; }
}