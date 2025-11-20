using SoundFlow.Providers;
using SoundFlow.Utils;

namespace SoundFlow.Editing;

/// <summary>
/// Represents a single MIDI segment (clip) placed on a timeline within a MIDI track.
/// It references a MidiDataProvider and applies playback settings.
/// </summary>
public sealed class MidiSegment : IDisposable
{
    private MidiDataProvider _playbackProviderCache;
    private volatile bool _isDirty;
    private readonly object _providerLock = new();

    /// <summary>
    /// Gets or sets the name of the MIDI segment.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets the mutable sequence of MIDI data for this segment.
    /// </summary>
    public MidiSequence Sequence { get; }

    /// <summary>
    /// Gets a read-only <see cref="MidiDataProvider"/> for the playback engine.
    /// This provider is cached and regenerated in a thread-safe manner only when the underlying sequence changes.
    /// </summary>
    public MidiDataProvider DataProvider
    {
        get
        {
            if (!_isDirty)
                return _playbackProviderCache;

            // If the data is dirty, acquire a lock to ensure thread-safe regeneration.
            lock (_providerLock)
            {
                if (!_isDirty) return _playbackProviderCache;
                _playbackProviderCache = new MidiDataProvider(Sequence);
                    
                _isDirty = false;
                return _playbackProviderCache;
            }
        }
    }

    /// <summary>
    /// Gets or sets the starting time of this segment on the overall composition timeline.
    /// </summary>
    public TimeSpan TimelineStartTime { get; set; }
    
    /// <summary>
    /// Gets the duration of the MIDI data within this segment, calculated based on the parent composition's tempo map.
    /// </summary>
    public TimeSpan SourceDuration => ParentTrack?.ParentComposition != null 
        ? MidiTimeConverter.GetTimeSpanForTick(DataProvider.LengthTicks, DataProvider.TicksPerQuarterNote, ParentTrack.ParentComposition.TempoTrack) 
        : TimeSpan.Zero;
    
    /// <summary>
    /// Gets the end time of this segment on the overall composition timeline.
    /// </summary>
    public TimeSpan TimelineEndTime => TimelineStartTime + SourceDuration;
    
    /// <summary>
    /// Gets or sets the parent track to which this segment is added.
    /// </summary>
    public MidiTrack? ParentTrack { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiSegment"/> class using a mutable <see cref="MidiSequence"/>.
    /// </summary>
    /// <param name="sequence">The mutable MIDI sequence. Cannot be null.</param>
    /// <param name="timelineStartTime">The starting time on the composition timeline.</param>
    /// <param name="name">Optional name for the segment.</param>
    public MidiSegment(MidiSequence sequence, TimeSpan timelineStartTime, string name = "MIDI Segment")
    {
        Sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
        TimelineStartTime = timelineStartTime;
        Name = name;
        
        // Initialize the cache on creation.
        _playbackProviderCache = new MidiDataProvider(Sequence);
        _isDirty = false;
    }

    /// <summary>
    /// Marks the segment's data as having been changed.
    /// This will trigger regeneration of the playback data provider on its next access and mark the composition as dirty.
    /// </summary>
    public void MarkDirty()
    {
        _isDirty = true;
        ParentTrack?.MarkDirty();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // MidiDataProvider does not currently have any disposable content, maybe later.
    }
}