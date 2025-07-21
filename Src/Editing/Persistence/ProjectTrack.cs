namespace SoundFlow.Editing.Persistence;

/// <summary>
/// Serializable representation of a <see cref="Track"/> for project files.
/// This DTO captures the essential properties of an audio track for persistence,
/// including its segments and track-level settings.
/// </summary>
public class ProjectTrack
{
    /// <summary>
    /// Gets or sets the name of the track.
    /// </summary>
    public string Name { get; set; } = "Track";

    /// <summary>
    /// Gets or sets the list of <see cref="ProjectSegment"/>s contained within this track.
    /// These segments define the audio content and arrangement on the track.
    /// </summary>
    public List<ProjectSegment> Segments { get; set; } = [];

    /// <summary>
    /// Gets or sets the configurable settings for this track,
    /// such as master volume, pan, and mute/solo states.
    /// </summary>
    public ProjectTrackSettings Settings { get; set; } = new();
}
