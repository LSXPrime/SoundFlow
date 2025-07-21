using SoundFlow.Abstracts;

namespace SoundFlow.Editing;

/// <summary>
/// Represents the configurable settings for a <see cref="Track"/>,
/// controlling its overall playback characteristics such as volume, pan, and mute/solo states.
/// </summary>
public class TrackSettings
{
    private float _volume = 1.0f;
    private float _pan;
    private bool _isMuted;
    private bool _isSoloed;
    private bool _isEnabled = true;

    /// <summary>
    /// Gets or sets the parent <see cref="Track"/> that owns these settings.
    /// This is used internally to propagate dirty state.
    /// </summary>
    internal Track? ParentTrack { get; set; }

    /// <summary>
    /// Gets the chain of sound modifiers (effects) to be applied to this track.
    /// These are applied after all segments on the track are mixed.
    /// </summary>
    public List<SoundModifier> Modifiers { get; init; } = [];

    /// <summary>
    /// Gets the chain of audio analyzers to process this track's mixed audio.
    /// Analyzers process the audio after all track modifiers have been applied.
    /// </summary>
    public List<AudioAnalyzer> Analyzers { get; init; } = [];

    /// <summary>
    /// Gets or sets the master volume level for the track.
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
    /// Gets or sets the stereo pan position for the entire track.
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
    /// Gets or sets a value indicating whether the track is muted.
    /// If true, the track will produce no audio output.
    /// </summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value) return;
            _isMuted = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the track is soloed.
    /// If true, only this track (and other soloed tracks) will produce audio output
    /// in the composition. Non-soloed tracks will be muted.
    /// </summary>
    public bool IsSoloed
    {
        get => _isSoloed;
        set
        {
            if (_isSoloed == value) return;
            _isSoloed = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the track is enabled for playback.
    /// If false, the track will produce silence, regardless of mute/solo settings.
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
    /// Creates a shallow clone of the current <see cref="TrackSettings"/> instance.
    /// The cloned instance will have the same scalar property values and copies of the Modifiers and Analyzers lists,
    /// but its <see cref="ParentTrack"/> will be null.
    /// </summary>
    /// <returns>A new <see cref="TrackSettings"/> object with the same property values.</returns>
    public TrackSettings Clone()
    {
        var clone = (TrackSettings)MemberwiseClone();
        clone.Modifiers.AddRange(Modifiers);
        clone.Analyzers.AddRange(Analyzers);
        clone.ParentTrack = null;
        return clone;
    }

    /// <summary>
    /// Marks the parent track as dirty, propagating the change up the hierarchy to the composition.
    /// </summary>
    private void MarkDirty()
    {
        ParentTrack?.MarkDirty();
    }

    #region Modifier/Analyzer Management

    /// <summary>
    /// Adds a <see cref="SoundModifier"/> to the end of the track's modifier chain.
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
    /// Removes a specific <see cref="SoundModifier"/> from the track's modifier chain.
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
    /// Reorders a <see cref="SoundModifier"/> within the track's modifier chain to a new index.
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
    /// Adds an <see cref="AudioAnalyzer"/> to the end of the track's analyzer chain.
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
    /// Removes a specific <see cref="AudioAnalyzer"/> from the track's analyzer chain.
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
