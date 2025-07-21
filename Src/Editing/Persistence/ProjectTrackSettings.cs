namespace SoundFlow.Editing.Persistence;

/// <summary>
/// Represents the configurable settings for a Track,
/// suitable for data transfer.
/// </summary>
public class ProjectTrackSettings
{
    /// <summary>
    /// Gets or sets the master volume level for the track.
    /// A value of 1.0f is normal volume.
    /// </summary>
    public float Volume { get; init; } = 1.0f;

    /// <summary>
    /// Gets or sets the stereo pan position for the entire track.
    /// A value of -1.0f means full left, 0.0f means center, and 1.0f means full right.
    /// </summary>
    public float Pan { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the track is muted.
    /// If true, the track will produce no audio output.
    /// </summary>
    public bool IsMuted { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the track is soloed.
    /// If true, only this track (and other soloed tracks) will produce audio output
    /// in the composition. Non-soloed tracks will be muted.
    /// </summary>
    public bool IsSoloed { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the track is enabled for playback.
    /// If false, the track will produce silence, regardless of mute/solo settings.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Gets the chain of sound modifiers (effects) to be applied to this track.
    /// These are represented as DTOs to carry their serializable data.
    /// </summary>
    public List<ProjectEffectData> Modifiers { get; init; } = [];

    /// <summary>
    /// Gets the chain of audio analyzers to process this track's mixed audio.
    /// These are represented as DTOs to carry their serializable data.
    /// </summary>
    public List<ProjectEffectData> Analyzers { get; init; } = [];
}
