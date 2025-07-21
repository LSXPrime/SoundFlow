namespace SoundFlow.Editing;

/// <summary>
/// Defines the looping behavior for an <see cref="AudioSegment"/>.
/// </summary>
public record struct LoopSettings
{
    /// <summary>
    /// Number of times the segment should repeat after its initial play.
    /// 0 means play once, 1 means play twice total, etc.
    /// MaxValue for infinite looping until target duration is met.
    /// </summary>
    public int Repetitions { get; }

    /// <summary>
    /// If specified, the segment will loop to fill this total duration,
    /// potentially cutting off the last loop partway.
    /// If null, Repetitions will be used.
    /// </summary>
    public TimeSpan? TargetDuration { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopSettings"/> struct.
    /// </summary>
    /// <param name="repetitions">The number of times the segment should repeat after its initial play.
    /// Clamped to be non-negative.</param>
    /// <param name="targetDuration">The total duration the segment should loop to fill.
    /// If null, <paramref name="repetitions"/> will be used.</param>
    public LoopSettings(int repetitions = 0, TimeSpan? targetDuration = null)
    {
        Repetitions = Math.Max(0, repetitions);
        TargetDuration = targetDuration;
    }

    /// <summary>
    /// Gets a <see cref="LoopSettings"/> instance configured for playing the segment once (no repetitions).
    /// </summary>
    public static LoopSettings PlayOnce => new(0);
}
