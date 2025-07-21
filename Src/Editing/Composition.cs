using System.Buffers;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SoundFlow.Editing;

/// <summary>
/// Represents a complete audio composition, acting as the top-level container for multiple tracks.
/// It combines audio from all tracks, applies master volume, and can be rendered as a single audio stream.
/// This class also implements <see cref="ISoundDataProvider"/>, allowing the entire composition
/// to be treated as a source for further processing or playback.
/// </summary>
public class Composition : ISoundDataProvider
{
    private string _name;
    private float _masterVolume = 1.0f;
    private int _sampleRate;
    private int _targetChannels;
    private int _currentReadPositionSamples;
    private bool _isDirty;
    
    /// <summary>
    /// Gets the audio format of the composition.
    /// </summary>
    public AudioFormat Format { get; }

    /// <summary>
    /// Gets or sets the name of the composition.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets the chain of sound modifiers (effects) to be applied to the master output of this composition.
    /// These are applied after all tracks are mixed together.
    /// </summary>
    public List<SoundModifier> Modifiers { get; init; } = [];

    /// <summary>
    /// Gets the chain of audio analyzers to process the master output of this composition.
    /// Analyzers process the audio after all master modifiers have been applied.
    /// </summary>
    public List<AudioAnalyzer> Analyzers { get; init; } = [];

    /// <summary>
    /// Gets the list of <see cref="Track"/>s contained within this composition.
    /// </summary>
    public List<Track> Tracks { get; } = [];

    /// <summary>
    /// Gets or sets the master volume level for the entire composition.
    /// A value of 1.0f is normal volume. Values greater than 1.0f can lead to clipping.
    /// </summary>
    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            if (!(Math.Abs(_masterVolume - value) > 0.0001f)) return;
            _masterVolume = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the composition has unsaved changes.
    /// This flag is set to true when modifications are made and reset after saving.
    /// </summary>
    public bool IsDirty => _isDirty;
    
    /// <summary>
    /// Gets or sets the target sample rate for rendering this composition.
    /// This defines the output sample rate when the composition is rendered or read as an <see cref="ISoundDataProvider"/>.
    /// </summary>
    public int SampleRate
    {
        get => _sampleRate;
        set
        {
            if (_sampleRate == value) return;
            _sampleRate = value;
            MarkDirty();
        }
    }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Gets or sets the target number of channels for rendering this composition.
    /// This defines the output channel count when the composition is rendered or read as an <see cref="ISoundDataProvider"/>.
    /// </summary>
    public int TargetChannels
    {
        get => _targetChannels;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Channels must be greater than 0.");
            if (_targetChannels == value) return;
            _targetChannels = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Composition"/> class.
    /// </summary>
    /// <param name="format">The audio format of the composition. Cannot be null.</param>
    /// <param name="name">The name of the composition. Defaults to "Composition".</param>
    /// <param name="targetChannels">Optional target number of channels for the composition's output. If null, uses the engine's default.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="targetChannels"/> is less than or equal to 0.</exception>
    public Composition(AudioFormat format, string name = "Composition", int? targetChannels = null)
    {
        Format = format;
        _name = name;
        _sampleRate = format.SampleRate;
        if (targetChannels is <= 0) throw new ArgumentOutOfRangeException(nameof(targetChannels), "Channels must be greater than 0.");
        _targetChannels = targetChannels ?? format.Channels;
    }

    /// <summary>
    /// Adds a <see cref="Track"/> to the composition.
    /// </summary>
    /// <param name="track">The track to add.</param>
    public void AddTrack(Track track)
    {
        track.ParentComposition = this;
        Tracks.Add(track);
        MarkDirty();
    }

    /// <summary>
    /// Removes a <see cref="Track"/> from the composition.
    /// </summary>
    /// <param name="track">The track to remove.</param>
    /// <returns>True if the track was successfully removed, false otherwise.</returns>
    public bool RemoveTrack(Track track)
    {
        track.ParentComposition = null;
        var removed = Tracks.Remove(track);
        if (removed) MarkDirty();
        return removed;
    }

    /// <summary>
    /// Calculates the total duration of the composition, determined by the end time of the longest track.
    /// </summary>
    /// <returns>A <see cref="TimeSpan"/> representing the total duration of the composition.</returns>
    public TimeSpan CalculateTotalDuration()
    {
        return Tracks.Count == 0 ? TimeSpan.Zero : Tracks.Max(t => t.CalculateDuration());
    }

    /// <summary>
    /// Renders a specific time portion of the composition into a new float array.
    /// </summary>
    /// <param name="startTime">The global timeline start time of the portion to render.</param>
    /// <param name="duration">The duration of the audio to render.</param>
    /// <returns>A float array containing the rendered audio samples. An empty array is returned if no samples are rendered.</returns>
    public float[] Render(TimeSpan startTime, TimeSpan duration)
    {
        var samplesToRender = (int)(duration.TotalSeconds * SampleRate * TargetChannels);
        if (samplesToRender <= 0) return [];

        var outputBuffer = new float[samplesToRender];
        Render(startTime, duration, outputBuffer.AsSpan());
        return outputBuffer;
    }

    /// <summary>
    /// Renders a specific time portion of the composition into a provided buffer.
    /// This method mixes audio from all active tracks, applies master volume, and performs clipping.
    /// </summary>
    /// <param name="startTime">The global timeline start time of the portion to render.</param>
    /// <param name="duration">The duration of the audio to render.</param>
    /// <param name="outputBuffer">The span to fill with rendered audio samples. This buffer will be cleared before rendering.</param>
    /// <returns>The number of samples actually written to the output buffer.</returns>
    public int Render(TimeSpan startTime, TimeSpan duration, Span<float> outputBuffer)
    {
        var samplesToRender = (int)(duration.TotalSeconds * SampleRate * TargetChannels);
        samplesToRender = Math.Min(samplesToRender, outputBuffer.Length);
        if (samplesToRender <= 0) return 0;

        outputBuffer[..samplesToRender].Clear(); // Initialize with silence

        var activeTracks = GetActiveTracksForRendering();
            
        float[]? trackBuffer = null;

        try
        {
            foreach (var track in activeTracks)
            {
                // Ensure trackBuffer is large enough or allocate a new one from the pool
                if (trackBuffer == null || trackBuffer.Length < samplesToRender)
                {
                    if(trackBuffer != null) ArrayPool<float>.Shared.Return(trackBuffer);
                    trackBuffer = ArrayPool<float>.Shared.Rent(samplesToRender);
                }
                var trackBufferSpan = trackBuffer.AsSpan(0, samplesToRender);
                    
                track.Render(startTime, duration, trackBufferSpan, SampleRate, TargetChannels);

                // Mix trackBuffer into outputBuffer
                for (var i = 0; i < samplesToRender; i++)
                {
                    outputBuffer[i] += trackBufferSpan[i];
                }
            }
        }
        finally
        {
            if (trackBuffer != null)
            {
                ArrayPool<float>.Shared.Return(trackBuffer);
            }
        }

        // Apply Composition (Master) Modifiers
        foreach (var modifier in Modifiers)
        {
            if (modifier.Enabled)
            {
                modifier.Process(outputBuffer[..samplesToRender], TargetChannels);
            }
        }

        // Process Composition (Master) Analyzers
        foreach (var analyzer in Analyzers)
        {
            analyzer.Process(outputBuffer[..samplesToRender], TargetChannels);
        }

        // Apply master volume and clipping to the final mixed output
        for (var i = 0; i < samplesToRender; i++)
        {
            outputBuffer[i] *= MasterVolume;
            outputBuffer[i] = Math.Clamp(outputBuffer[i], -1.0f, 1.0f);
        }
        
        return samplesToRender;
    }

    /// <summary>
    /// Determines which tracks are active for rendering based on their mute, solo, and enabled states.
    /// If any track is soloed, only soloed tracks are returned. Otherwise, all non-muted, enabled tracks are returned.
    /// </summary>
    /// <returns>A list of <see cref="Track"/> objects that should be included in the rendering process.</returns>
    private List<Track> GetActiveTracksForRendering()
    {
        var soloedTracks = Tracks.Where(t => t.Settings is { IsSoloed: true, IsEnabled: true, IsMuted: false }).ToList();
        return soloedTracks.Count != 0 ? soloedTracks : Tracks.Where(t => t.Settings is { IsMuted: false, IsEnabled: true }).ToList();
    }

    /// <summary>
    /// Gets the current read position within the composition in total samples.
    /// </summary>
    public int Position => _currentReadPositionSamples;

    /// <inheritdoc />
    public int Length => (int)(CalculateTotalDuration().TotalSeconds * SampleRate * TargetChannels); // Length in total samples
    
    /// <inheritdoc />
    public bool CanSeek => true;

    /// <inheritdoc />
    public SampleFormat SampleFormat => SampleFormat.F32; // Output is always float

    /// <inheritdoc />
    public event EventHandler<EventArgs>? EndOfStreamReached;

    /// <inheritdoc />
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
    
    /// <inheritdoc />
    public int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed) return 0;
        var currentTime = TimeSpan.FromSeconds((double)_currentReadPositionSamples / SampleRate / TargetChannels);
        var durationToRead = TimeSpan.FromSeconds((double)buffer.Length / SampleRate / TargetChannels);
        var totalDuration = CalculateTotalDuration();

        if (currentTime >= totalDuration)
        {
            EndOfStreamReached?.Invoke(this, EventArgs.Empty);
            return 0; // End of composition
        }

        // Adjust durationToRead if it would go past the end of the composition
        if (currentTime + durationToRead > totalDuration)
        {
            durationToRead = totalDuration - currentTime;
        }
            
        var samplesWritten = Render(currentTime, durationToRead, buffer);
            
        _currentReadPositionSamples += samplesWritten;
        PositionChanged?.Invoke(this, new PositionChangedEventArgs(_currentReadPositionSamples));

        if (samplesWritten < buffer.Length || currentTime + durationToRead >= totalDuration)
        {
            EndOfStreamReached?.Invoke(this, EventArgs.Empty);
        }
        return samplesWritten;
    }

    /// <inheritdoc />
    public void Seek(int sampleOffset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        
        _currentReadPositionSamples = Math.Clamp(sampleOffset, 0, Length);
        PositionChanged?.Invoke(this, new PositionChangedEventArgs(_currentReadPositionSamples));
    }

    /// <summary>
    /// Disposes of all <see cref="AudioSegment"/>s across all tracks that own their sound data providers.
    /// This releases unmanaged resources held by data providers.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        foreach (var segment in Tracks.SelectMany(track => track.Segments))
        {
            segment.Dispose();
        }

        Tracks.Clear(); Modifiers.Clear(); Analyzers.Clear();
        Tracks.Clear();
        GC.SuppressFinalize(this);
        IsDisposed = true;
    }

    /// <summary>
    /// Replaces the source audio content of an existing <see cref="AudioSegment"/> on a track.
    /// The original segment's source data provider will be disposed if it was owned by the segment.
    /// </summary>
    /// <param name="track">The track containing the segment to replace.</param>
    /// <param name="originalStartTime">The exact timeline start time of the segment to find and replace.</param>
    /// <param name="originalEndTime">The exact timeline end time of the segment to find and replace.</param>
    /// <param name="replacementSource">The new sound data provider for the segment. Cannot be null.</param>
    /// <param name="replacementSourceStartTime">The new starting time offset within the <paramref name="replacementSource"/>.</param>
    /// <param name="replacementSourceDuration">The new duration of the audio to read from the <paramref name="replacementSource"/>.</param>
    /// <returns>True if the segment was found and its source replaced, false otherwise.</returns>
    public bool ReplaceSegment(Track track, TimeSpan originalStartTime, TimeSpan originalEndTime, ISoundDataProvider replacementSource, TimeSpan replacementSourceStartTime, TimeSpan replacementSourceDuration)
    {
        var segmentToReplace = track.Segments.FirstOrDefault(s => s.TimelineStartTime == originalStartTime && s.TimelineEndTime == originalEndTime);
        if (segmentToReplace == null) return false;

        segmentToReplace.ReplaceSource(replacementSource, replacementSourceStartTime, replacementSourceDuration);
        MarkDirty();
        return true;
    }

    /// <summary>
    /// Removes a specific <see cref="AudioSegment"/> from a track.
    /// </summary>
    /// <param name="track">The track from which to remove the segment.</param>
    /// <param name="segmentToRemove">The audio segment instance to remove.</param>
    /// <param name="shiftFollowing">
    /// If true, subsequent segments on the track will be shifted earlier to close the gap.
    /// </param>
    /// <returns>True if the segment was found and removed, false otherwise.</returns>
    public bool RemoveSegment(Track track, AudioSegment segmentToRemove, bool shiftFollowing = true)
    {
        var removed = track.RemoveSegment(segmentToRemove, shiftFollowing);
        if (removed) MarkDirty();
        return removed;
    }
        
    /// <summary>
    /// Removes an <see cref="AudioSegment"/> from a track identified by its timeline start and end times.
    /// </summary>
    /// <param name="track">The track from which to remove the segment.</param>
    /// <param name="startTime">The exact timeline start time of the segment to remove.</param>
    /// <param name="endTime">The exact timeline end time of the segment to remove.</param>
    /// <param name="shiftFollowing">
    /// If true, subsequent segments on the track will be shifted earlier to close the gap.
    /// </param>
    /// <returns>True if the segment was found and removed, false otherwise.</returns>
    public bool RemoveSegment(Track track, TimeSpan startTime, TimeSpan endTime, bool shiftFollowing = true)
    {
        var segment = track.Segments.FirstOrDefault(s => s.TimelineStartTime == startTime && s.TimelineEndTime == endTime);
        var removed = segment != null && track.RemoveSegment(segment, shiftFollowing);
        if (removed) MarkDirty();
        return removed;
    }
    
    /// <summary>
    /// Silences a specified time range on a given track by manipulating existing segments
    /// and inserting a new silent segment.
    /// This operation is non-destructive to original sources. It may:
    /// <list type="bullet">
    ///     <item><description>Remove segments fully contained within the silence range.</description></item>
    ///     <item><description>Trim segments that partially overlap the start or end of the silence range.</description></item>
    ///     <item><description>Split segments that span across the entire silence range into two, before and after the silenced part.</description></item>
    /// </list>
    /// An explicit silent audio segment is then inserted to cover the specified range.
    /// The overall timing of other audio on the track (outside the silenced range) remains unchanged.
    /// </summary>
    /// <param name="track">The track to apply the silence to.</param>
    /// <param name="rangeStartTime">The start time of the range to silence on the track.</param>
    /// <param name="rangeDuration">The duration of the silence.</param>
    /// <returns>The newly created silent <see cref="AudioSegment"/> that fills the silenced range, or null if <paramref name="rangeDuration"/> is zero or negative.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="track"/> is null.</exception>
    public AudioSegment? SilenceSegment(Track track, TimeSpan rangeStartTime, TimeSpan rangeDuration)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (rangeDuration <= TimeSpan.Zero) return null;

        var rangeEndTime = rangeStartTime + rangeDuration;
        var segmentsToAdd = new List<AudioSegment>();

        // Create the explicit silent segment that will fill the silenced range
        var silentSamplesCount = (int)(rangeDuration.TotalSeconds * SampleRate * TargetChannels);
        silentSamplesCount = Math.Max(0, silentSamplesCount); 
        
        // Create a RawDataProvider with silence (all zeros) for the new segment
        var silentDataProvider = new RawDataProvider(new float[silentSamplesCount]);
        var mainSilentSegment = new AudioSegment(
            Format,
            silentDataProvider,
            TimeSpan.Zero,
            rangeDuration,
            rangeStartTime,
            "Silent Section",
            settings: null,
            ownsDataProvider: true
        );

        // Iterate backwards through a copy of the list to safely modify/remove segments
        var segmentsOnTrack = track.Segments.ToList();
        for (var i = segmentsOnTrack.Count - 1; i >= 0; i--)
        {
            var segment = segmentsOnTrack[i];
            var segmentTimelineStart = segment.TimelineStartTime;
            // Calculate actual end time including speed factor and loops
            var segmentTimelineEnd = segment.TimelineStartTime + segment.GetTotalLoopedDurationOnTimeline();


            // Check for overlap, If none, segment ends before silence starts OR segment starts after silence ends
            if (segmentTimelineEnd <= rangeStartTime || segmentTimelineStart >= rangeEndTime)
                continue;


            // Case 1: Segment is completely enveloped by the silence range
            // [rangeStart----[segmentStart----segmentEnd]----rangeEnd]
            if (segmentTimelineStart >= rangeStartTime && segmentTimelineEnd <= rangeEndTime)
            {
                track.Segments.Remove(segment);
                segment.Dispose();
                continue;
            }

            // Case 2: Segment is split by the silence range
            // [segmentStart----[rangeStart----rangeEnd]----segmentEnd]
            if (segmentTimelineStart < rangeStartTime && segmentTimelineEnd > rangeEndTime)
            {
                // Part 1 (before silence): Modify the original segment
                var part1TimelineDuration = rangeStartTime - segmentTimelineStart;
                var part1SourceDuration = TimeSpan.FromTicks((long)(part1TimelineDuration.Ticks * segment.Settings.SpeedFactor));
                
                // Part 3 (after silence): Create a new segment
                var part3TimelineStart = rangeEndTime;
                var part3OriginalTimelineOffset = rangeEndTime - segmentTimelineStart; // Offset from original segment's start on timeline
                var part3SourceOffsetFromOriginalSourceStart = TimeSpan.FromTicks((long)(part3OriginalTimelineOffset.Ticks * segment.Settings.SpeedFactor));

                var part3SourceStartTime = segment.SourceStartTime + part3SourceOffsetFromOriginalSourceStart;
                var part3SourceDuration = segment.SourceDuration - part3SourceOffsetFromOriginalSourceStart;


                if (part3SourceDuration > TimeSpan.Zero)
                {
                    var part3Segment = new AudioSegment(
                        Format,
                        segment.SourceDataProvider,
                        part3SourceStartTime,
                        part3SourceDuration,
                        part3TimelineStart,
                        $"{segment.Name} (After Silence)",
                        segment.Settings.Clone()
                    );
                    segmentsToAdd.Add(part3Segment);
                }

                segment.SourceDuration = part1SourceDuration;
                continue;
            }

            // Case 3: Segment overlaps the start of the silence range (tail of segment is silenced)
            // [segmentStart----[rangeStart----segmentEnd]----rangeEnd]
            if (segmentTimelineStart < rangeStartTime && segmentTimelineEnd > rangeStartTime)
            {
                var newTimelineDuration = rangeStartTime - segmentTimelineStart;
                var newSourceDuration = TimeSpan.FromTicks((long)(newTimelineDuration.Ticks * segment.Settings.SpeedFactor));
                
                if (newSourceDuration <= TimeSpan.Zero)
                {
                    track.Segments.Remove(segment);
                    segment.Dispose();
                }
                else
                {
                    segment.SourceDuration = newSourceDuration;
                }
                continue;
            }

            // Case 4: Segment overlaps the end of the silence range (head of segment is silenced)
            // [rangeStart----[segmentStart----rangeEnd]----segmentEnd]
            if (segmentTimelineStart >= rangeStartTime && segmentTimelineStart < rangeEndTime)
            {
                var oldTimelineStart = segment.TimelineStartTime;

                var timelineShiftAmount = rangeEndTime - oldTimelineStart;
                var sourceTimeShiftAmount = TimeSpan.FromTicks((long)(timelineShiftAmount.Ticks * segment.Settings.SpeedFactor));

                segment.TimelineStartTime = rangeEndTime;
                segment.SourceStartTime += sourceTimeShiftAmount;
                segment.SourceDuration -= sourceTimeShiftAmount;

                if (segment.SourceDuration <= TimeSpan.Zero)
                {
                    track.Segments.Remove(segment);
                    segment.Dispose();
                }
            }
        }

        // Add any newly created split segments to the actual track
        foreach (var newSeg in segmentsToAdd)
        {
            track.AddSegment(newSeg);
        }

        // Add the main silent segment to the actual track
        track.AddSegment(mainSilentSegment);

        MarkDirty();
        return mainSilentSegment;
    }

    /// <summary>
    /// Inserts an <see cref="AudioSegment"/> into a specified track at a given insertion point.
    /// </summary>
    /// <param name="track">The track into which the segment should be inserted.</param>
    /// <param name="insertionPoint">The timeline point at which to insert the segment.</param>
    /// <param name="segmentToInsert">The audio segment to insert.</param>
    /// <param name="shiftFollowing">
    /// If true, all segments on the track that start at or after the insertion point
    /// will be shifted later by the duration of the inserted segment.
    /// </param>
    public void InsertSegment(Track track, TimeSpan insertionPoint, AudioSegment segmentToInsert, bool shiftFollowing = true)
    {
        track.InsertSegmentAt(segmentToInsert, insertionPoint, shiftFollowing);
        MarkDirty();
    }
    
    /// <summary>
    /// Marks the composition as dirty (having unsaved changes).
    /// This should be called by any method that modifies the composition's state.
    /// </summary>
    public void MarkDirty()
    {
        _isDirty = true;
    }

    /// <summary>
    /// Clears the dirty flag, typically after a successful save operation.
    /// </summary>
    internal void ClearDirtyFlag()
    {
        _isDirty = false;
    }

    #region Modifier/Analyzer Management

    /// <summary>
    /// Adds a <see cref="SoundModifier"/> to the end of the composition's master modifier chain.
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
    /// Removes a specific <see cref="SoundModifier"/> from the composition's master modifier chain.
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
    /// Reorders a <see cref="SoundModifier"/> within the composition's master modifier chain to a new index.
    /// </summary>
    /// <param name="modifier">The modifier to reorder.</param>
    /// <param name="newIndex">The zero-based index where the modifier should be moved to.</param>
    public void ReorderModifier(SoundModifier modifier, int newIndex)
    {
        if (Modifiers.Remove(modifier))
        {
            Modifiers.Insert(Math.Clamp(newIndex, 0, Modifiers.Count), modifier);
            MarkDirty();
        }
    }
    
    /// <summary>
    /// Adds an <see cref="AudioAnalyzer"/> to the end of the composition's master analyzer chain.
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
    /// Removes a specific <see cref="AudioAnalyzer"/> from the composition's master analyzer chain.
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
