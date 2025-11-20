namespace SoundFlow.Editing.Persistence;

/// <summary>
/// Serializable representation of a <see cref="MidiTrack"/> for project files.
/// </summary>
public class ProjectMidiTrack
{
    /// <summary>
    /// Gets or sets the name of the track.
    /// </summary>
    public string Name { get; set; } = "MIDI Track";

    /// <summary>
    /// Gets or sets the list of <see cref="ProjectMidiSegment"/>s contained within this track.
    /// </summary>
    public List<ProjectMidiSegment> Segments { get; set; } = [];

    /// <summary>
    /// Gets or sets the name of the target component for this track's MIDI output.
    /// This name is used to resolve the reference to the actual component instance during project loading.
    /// </summary>
    public string? TargetComponentName { get; set; }
    
    /// <summary>
    /// Gets or sets the configurable settings for this track.
    /// </summary>
    public ProjectTrackSettings Settings { get; set; } = new();
}