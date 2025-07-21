using SoundFlow.Abstracts;

namespace SoundFlow.Editing;

/// <summary>
/// Represents the configurable settings for an <see cref="AudioSegment"/>,
/// controlling its playback characteristics such as volume, pan, fades, looping, and speed.
/// </summary>
public class AudioSegmentSettings
{
    private float _volume = 1.0f;
    private float _pan;
    private TimeSpan _fadeInDuration = TimeSpan.Zero;
    private FadeCurveType _fadeInCurve = FadeCurveType.Linear;
    private TimeSpan _fadeOutDuration = TimeSpan.Zero;
    private FadeCurveType _fadeOutCurve = FadeCurveType.Linear;
    private bool _isReversed;
    private LoopSettings _loop = LoopSettings.PlayOnce;
    private float _speedFactor = 1.0f;
    private bool _isEnabled = true;
    private float _timeStretchFactor = 1.0f;
    private TimeSpan? _targetStretchDuration;
    
    /// <summary>
    /// Gets or sets the parent <see cref="AudioSegment"/> that owns these settings.
    /// This is used internally to propagate dirty state.
    /// </summary>
    internal AudioSegment? ParentSegment { get; set; }

    /// <summary>
    /// Gets the chain of sound modifiers (effects) to be applied to this segment.
    /// Modifiers are applied in the order they appear in this list.
    /// </summary>
    public List<SoundModifier> Modifiers { get; init; } = [];

    /// <summary>
    /// Gets the chain of audio analyzers to process this segment's audio.
    /// Analyzers process the audio after all segment modifiers have been applied.
    /// </summary>
    public List<AudioAnalyzer> Analyzers { get; init; } = [];

    /// <summary>
    /// Gets or sets the volume level of the segment.
    /// A value of 1.0f is normal volume.
    /// </summary>
    public float Volume
    {
        get => _volume;
        set
        {
            if (Math.Abs(_volume - value) < float.Epsilon) return;
            _volume = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets the stereo pan position of the segment.
    /// A value of -1.0f means full left, 0.0f means center, and 1.0f means full right.
    /// </summary>
    public float Pan
    {
        get => _pan;
        set
        {
            if (Math.Abs(_pan - value) < float.Epsilon) return;
            _pan = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets the duration of the fade-in effect at the beginning of the segment.
    /// If set to TimeSpan.Zero, no fade-in is applied.
    /// </summary>
    public TimeSpan FadeInDuration
    {
        get => _fadeInDuration;
        set
        {
            if (_fadeInDuration == value) return;
            _fadeInDuration = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets the curve type used for the fade-in effect.
    /// </summary>
    public FadeCurveType FadeInCurve
    {
        get => _fadeInCurve;
        set
        {
            if (_fadeInCurve == value) return;
            _fadeInCurve = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets the duration of the fade-out effect at the end of the segment.
    /// If set to TimeSpan.Zero, no fade-out is applied.
    /// </summary>
    public TimeSpan FadeOutDuration
    {
        get => _fadeOutDuration;
        set
        {
            if (_fadeOutDuration == value) return;
            _fadeOutDuration = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets the curve type used for the fade-out effect.
    /// </summary>
    public FadeCurveType FadeOutCurve
    {
        get => _fadeOutCurve;
        set
        {
            if (_fadeOutCurve == value) return;
            _fadeOutCurve = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the segment's audio should be played in reverse.
    /// </summary>
    public bool IsReversed
    {
        get => _isReversed;
        set
        {
            if (_isReversed == value) return;
            _isReversed = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets the looping settings for the segment, defining how it repeats.
    /// </summary>
    public LoopSettings Loop
    {
        get => _loop;
        set
        {
            // Assuming LoopSettings is a struct or an immutable class for simple comparison
            if (_loop.Equals(value)) return;
            _loop = value;
            MarkDirty();
        }
    }
    
    /// <summary>
    /// Playback speed factor (affects pitch and tempo). 1.0 is normal.
    /// </summary>
    public float SpeedFactor
    {
        get => _speedFactor;
        set
        {
            if (Math.Abs(_speedFactor - value) < float.Epsilon) return;
            _speedFactor = value;
            MarkDirty();
        }
    }


    /// <summary>
    /// Gets or sets the time stretch factor for pitch-preserved stretching.
    /// 1.0 = no stretch, more than 1.0 = longer duration, less than 1.0 = shorter duration.
    /// If <see cref="TargetStretchDuration"/> is set, this factor is derived from it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the value is less than or equal to zero.</exception>
    public float TimeStretchFactor
    {
        get => _targetStretchDuration.HasValue && ParentSegment != null && ParentSegment.SourceDuration > TimeSpan.Zero
            ? (float)(_targetStretchDuration.Value.TotalSeconds / ParentSegment.SourceDuration.TotalSeconds)
            : _timeStretchFactor;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "TimeStretchFactor must be greater than zero.");
            if (!(Math.Abs(_timeStretchFactor - value) > float.Epsilon) && !_targetStretchDuration.HasValue) return;
            _timeStretchFactor = value;
            _targetStretchDuration = null;
            MarkDirty();
            ParentSegment?.FullResetState();
        }
    }

    /// <summary>
    /// Gets or sets the target duration for pitch-preserved time stretching.
    /// If set, this overrides <see cref="TimeStretchFactor"/>.
    /// Set to null to use <see cref="TimeStretchFactor"/> instead.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if a non-null value is set to TimeSpan.Zero or negative.</exception>
    public TimeSpan? TargetStretchDuration
    {
        get => _targetStretchDuration;
        set
        {
            if (value.HasValue && value.Value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value), "TargetStretchDuration must be positive if set.");
            if (_targetStretchDuration == value) return;
            _targetStretchDuration = value;
            // _timeStretchFactor will be dynamically calculated if this is set.
            MarkDirty();
            ParentSegment?.FullResetState();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the segment is enabled for playback.
    /// If false, the segment will produce silence, regardless of other settings.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Creates a shallow clone of the current <see cref="AudioSegmentSettings"/> instance.
    /// The cloned instance will have the same property values but will not have a <see cref="ParentSegment"/> assigned.
    /// </summary>
    /// <returns>A new <see cref="AudioSegmentSettings"/> object with the same property values.</returns>
    public AudioSegmentSettings Clone()
    {
        var clone = (AudioSegmentSettings)MemberwiseClone();
        clone.ParentSegment = null;
        return clone;
    }

    /// <summary>
    /// Marks the parent segment as dirty, propagating the change up the hierarchy.
    /// </summary>
    private void MarkDirty()
    {
        ParentSegment?.MarkDirty();
    }

    #region Modifier/Analyzer Management

    /// <summary>
    /// Adds a <see cref="SoundModifier"/> to the end of the segment's modifier chain.
    /// </summary>
    /// <param name="modifier">The modifier to add. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="modifier"/> is null.</exception>
    public void AddModifier(SoundModifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        Modifiers.Add(modifier);
        MarkDirty();
    }

    /// <summary>
    /// Removes a specific <see cref="SoundModifier"/> from the segment's modifier chain.
    /// </summary>
    /// <param name="modifier">The modifier to remove.</param>
    /// <returns>True if the modifier was found and removed, false otherwise.</returns>
    public bool RemoveModifier(SoundModifier modifier)
    {
        var removed = Modifiers.Remove(modifier);
        if (removed) MarkDirty();
        return removed;
    }

    /// <summary>
    /// Reorders a <see cref="SoundModifier"/> within the segment's modifier chain to a new index.
    /// </summary>
    /// <param name="modifier">The modifier to reorder.</param>
    /// <param name="newIndex">The zero-based index where the modifier should be moved to.</param>
    public void ReorderModifier(SoundModifier modifier, int newIndex)
    {
        if (!Modifiers.Remove(modifier)) return;
        Modifiers.Insert(Math.Clamp(newIndex, 0, Modifiers.Count), modifier);
        MarkDirty();
    }

    /// <summary>
    /// Adds an <see cref="AudioAnalyzer"/> to the end of the segment's analyzer chain.
    /// </summary>
    /// <param name="analyzer">The analyzer to add. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="analyzer"/> is null.</exception>
    public void AddAnalyzer(AudioAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        Analyzers.Add(analyzer);
        MarkDirty();
    }

    /// <summary>
    /// Removes a specific <see cref="AudioAnalyzer"/> from the segment's analyzer chain.
    /// </summary>
    /// <param name="analyzer">The analyzer to remove.</param>
    /// <returns>True if the analyzer was found and removed, false otherwise.</returns>
    public bool RemoveAnalyzer(AudioAnalyzer analyzer)
    {
        var removed = Analyzers.Remove(analyzer);
        if (removed) MarkDirty();
        return removed;
    }

    #endregion
}
