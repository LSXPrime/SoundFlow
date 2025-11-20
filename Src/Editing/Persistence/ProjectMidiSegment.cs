namespace SoundFlow.Editing.Persistence;

/// <summary>
/// Serializable representation of a <see cref="MidiSegment"/> for project files.
/// </summary>
public class ProjectMidiSegment
{
    /// <summary>
    /// Gets or sets the name of the MIDI segment.
    /// </summary>
    public string Name { get; set; } = "MIDI Segment";

    /// <summary>
    /// Gets or sets the unique identifier linking this segment to its
    /// corresponding MIDI data source defined in a <see cref="ProjectSourceReference"/>.
    /// </summary>
    public Guid SourceReferenceId { get; set; }
    
    /// <summary>
    /// Gets or sets the starting time of this segment on the overall composition timeline.
    /// </summary>
    public TimeSpan TimelineStartTime { get; set; }
}