using SoundFlow.Abstracts;

namespace SoundFlow.Editing.Persistence;

/// <summary>
/// Serializable representation of an <see cref="AudioSegment"/> for project files.
/// </summary>
public class ProjectSegment
{
    /// <summary>
    /// Gets or sets the name of the audio segment.
    /// </summary>
    public string Name { get; set; } = "Segment";

    /// <summary>
    /// Gets or sets the unique identifier linking this segment to its
    /// corresponding audio source defined in a <see cref="ProjectSourceReference"/>.
    /// </summary>
    public Guid SourceReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the starting time offset within the raw audio source data
    /// from which this segment begins reading.
    /// </summary>
    public TimeSpan SourceStartTime { get; set; }

    /// <summary>
    /// Gets or sets the duration of audio to read from the raw source data,
    /// starting from <see cref="SourceStartTime"/>.
    /// </summary>
    public TimeSpan SourceDuration { get; set; }

    /// <summary>
    /// Gets or sets the starting time of this segment on the overall composition timeline.
    /// </summary>
    public TimeSpan TimelineStartTime { get; set; }

    /// <summary>
    /// Gets or sets the configurable playback and effect settings for this audio segment.
    /// These settings control aspects like volume, pan, fades, looping, and speed.
    /// </summary>
    public ProjectAudioSegmentSettings Settings { get; set; } = new();
    
    /// <summary>
    /// Gets or sets the list of <see cref="ProjectEffectData"/> instances representing
    /// sound modifiers <see cref="SoundModifier"/> applied to this segment.
    /// </summary>
    public List<ProjectEffectData> Modifiers { get; set; } = [];
    
    /// <summary>
    /// Gets or sets the list of <see cref="ProjectEffectData"/> instances representing
    /// sound analyzers <see cref="AudioAnalyzer"/> applied to this segment.
    /// </summary>
    public List<ProjectEffectData> Analyzers { get; set; } = [];
}
