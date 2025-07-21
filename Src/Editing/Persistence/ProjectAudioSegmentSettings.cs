namespace SoundFlow.Editing.Persistence;

/// <summary>
/// Represents the serializable settings for an audio segment within a project file.
/// This Data Transfer Object (DTO) mirrors the runtime <see cref="SoundFlow.Editing.AudioSegmentSettings"/>
/// to facilitate persistence, excluding references to live runtime objects and including
/// serializable representations of modifiers and analyzers.
/// </summary>
public class ProjectAudioSegmentSettings
{
    /// <summary>
    /// Gets or sets the volume level of the segment. A value of 1.0f is normal volume.
    /// </summary>
    public float Volume { get; set; } = 1.0f;
    /// <summary>
    /// Gets or sets the stereo pan position for the segment.
    /// A value of -1.0f means full left, 0.0f means center, and 1.0f means full right.
    /// </summary>
    public float Pan { get; set; }
    /// <summary>
    /// Gets or sets the duration of the fade-in effect at the beginning of the segment.
    /// </summary>
    public TimeSpan FadeInDuration { get; set; }
    /// <summary>
    /// Gets or sets the curve type used for the fade-in effect.
    /// </summary>
    public FadeCurveType FadeInCurve { get; set; } = FadeCurveType.Linear;
    /// <summary>
    /// Gets or sets the duration of the fade-out effect at the end of the segment.
    /// </summary>
    public TimeSpan FadeOutDuration { get; set; }
    /// <summary>
    /// Gets or sets the curve type used for the fade-out effect.
    /// </summary>
    public FadeCurveType FadeOutCurve { get; set; } = FadeCurveType.Linear;
    /// <summary>
    /// Gets or sets a value indicating whether the segment's audio should be played in reverse.
    /// </summary>
    public bool IsReversed { get; set; }
    /// <summary>
    /// Gets or sets the looping settings for the segment.
    /// </summary>
    public LoopSettings Loop { get; set; } = LoopSettings.PlayOnce;
    /// <summary>
    /// Gets or sets the playback speed factor for the segment (affects both pitch and tempo). 1.0 is normal.
    /// </summary>
    public float SpeedFactor { get; set; } = 1.0f;
    /// <summary>
    /// Gets or sets a value indicating whether the segment is enabled for playback.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>
    /// Gets or sets the time stretch factor for pitch-preserved stretching.
    /// 1.0 means no stretch, more than 1.0 means longer duration, less than 1.0 means shorter duration.
    /// This value is overridden if <see cref="TargetStretchDuration"/> is specified.
    /// </summary>
    public float TimeStretchFactor { get; set; } = 1.0f;
    /// <summary>
    /// Gets or sets the target duration for pitch-preserved time stretching.
    /// If this value is set, it takes precedence over <see cref="TimeStretchFactor"/>.
    /// Set to null to use <see cref="TimeStretchFactor"/> instead.
    /// </summary>
    public TimeSpan? TargetStretchDuration { get; set; }
    
    /// <summary>
    /// Gets or sets the list of <see cref="ProjectEffectData"/> instances representing
    /// sound modifiers applied to this audio segment.
    /// </summary>
    public List<ProjectEffectData> Modifiers { get; set; } = [];
    /// <summary>
    /// Gets or sets the list of <see cref="ProjectEffectData"/> instances representing
    /// audio analyzers applied to this audio segment.
    /// </summary>
    public List<ProjectEffectData> Analyzers { get; set; } = [];

    /// <summary>
    /// Creates a new <see cref="ProjectAudioSegmentSettings"/> instance from a runtime <see cref="AudioSegmentSettings"/> object.
    /// Note: This method maps scalar properties. Modifiers and Analyzers are typically mapped externally (e.g., in <see cref="ProjectSegment"/>).
    /// </summary>
    /// <param name="settings">The runtime <see cref="AudioSegmentSettings"/> to convert.</param>
    /// <returns>A new <see cref="ProjectAudioSegmentSettings"/> DTO populated with data from the runtime settings.</returns>
    public static ProjectAudioSegmentSettings FromRuntime(AudioSegmentSettings settings)
    {
        return new ProjectAudioSegmentSettings
        {
            Volume = settings.Volume,
            Pan = settings.Pan,
            FadeInDuration = settings.FadeInDuration,
            FadeInCurve = settings.FadeInCurve,
            FadeOutDuration = settings.FadeOutDuration,
            FadeOutCurve = settings.FadeOutCurve,
            IsReversed = settings.IsReversed,
            Loop = settings.Loop,
            SpeedFactor = settings.SpeedFactor,
            IsEnabled = settings.IsEnabled,
            TimeStretchFactor = settings.TimeStretchFactor,
            TargetStretchDuration = settings.TargetStretchDuration
            // Modifiers and Analyzers would be mapped separately in ProjectSegment
        };
    }
}
