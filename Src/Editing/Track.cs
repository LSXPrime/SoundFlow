using System.Buffers;

namespace SoundFlow.Editing;

/// <summary>
/// Represents a single audio track within a composition, containing a collection of audio segments
/// and applying track-level settings like volume, pan, mute, and solo.
/// </summary>
public class Track
{
    private string _name;
    private TrackSettings _settings;
    private Composition? _parentComposition;
    
    /// <summary>
    /// Gets or sets the name of the track.
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
    /// Gets the list of <see cref="AudioSegment"/>s contained within this track.
    /// Segments are automatically sorted by their timeline start time.
    /// </summary>
    public List<AudioSegment> Segments { get; } = [];

    /// <summary>
    /// Gets or sets the settings applied to this track, such as volume, pan, mute, and solo.
    /// </summary>
    public TrackSettings Settings
    {
        get => _settings;
        set
        {
            if (_settings == value) return;
            _settings = value ?? throw new ArgumentNullException(nameof(value), "Settings cannot be null.");
            _settings.ParentTrack = this;
            MarkDirty();
        }
    }
    
    /// <summary>
    /// Gets or sets the parent composition to which this track belongs.
    /// </summary>
    internal Composition? ParentComposition
    {
        get => _parentComposition;
        set => _parentComposition = value;
    }


    /// <summary>
    /// Initializes a new instance of the <see cref="Track"/> class.
    /// </summary>
    /// <param name="name">The name of the track. Defaults to "Track".</param>
    /// <param name="settings">Optional initial settings for the track. If null, default settings are used.</param>
    public Track(string name = "Track", TrackSettings? settings = null)
    {
        _name = name;
        _settings = settings ?? new TrackSettings();
        _settings.ParentTrack = this;
    }


    /// <summary>
    /// Marks the parent composition as dirty (having unsaved changes).
    /// </summary>
    public void MarkDirty()
    {
        ParentComposition?.MarkDirty();
    }

    /// <summary>
    /// Adds an <see cref="AudioSegment"/> to the track.
    /// The track's segments are re-sorted after adding.
    /// </summary>
    /// <param name="segment">The audio segment to add.</param>
    public void AddSegment(AudioSegment segment)
    {
        segment.ParentTrack = this;
        segment.Settings.ParentSegment = segment;
        Segments.Add(segment);
        SortSegments();
        MarkDirty();
    }

    /// <summary>
    /// Removes an <see cref="AudioSegment"/> from the track.
    /// </summary>
    /// <param name="segment">The audio segment to remove.</param>
    /// <param name="shiftSubsequent">
    /// If true, all segments on the track that start at or after the removed segment's original start time
    /// will be shifted earlier by the duration of the removed segment. This closes the gap created by removal.
    /// </param>
    /// <returns>True if the segment was successfully removed, false otherwise.</returns>
    public bool RemoveSegment(AudioSegment segment, bool shiftSubsequent = false)
    {
        segment.ParentTrack = null;
        segment.Settings.ParentSegment = null;
        var removed = Segments.Remove(segment);
        if (removed)
        {
            if (shiftSubsequent)
            {
                var removedDuration = segment.EffectiveDurationOnTimeline;
                var subsequentSegments = Segments
                    .Where(s => s.TimelineStartTime >= segment.TimelineStartTime)
                    .OrderBy(s => s.TimelineStartTime)
                    .ToList();

                foreach (var subSegment in subsequentSegments)
                {
                    // TimelineStartTime property setter in AudioSegment will call subSegment.MarkDirty()
                    // which in turn calls this track's MarkDirty().
                    subSegment.TimelineStartTime -= removedDuration;
                }
            }
            SortSegments();
            MarkDirty();
        }
        return removed;
    }

    /// <summary>
    /// Inserts an <see cref="AudioSegment"/> into the track at a specified time.
    /// </summary>
    /// <param name="segmentToInsert">The audio segment to insert.</param>
    /// <param name="insertionTime">The timeline point at which to insert the segment.</param>
    /// <param name="shiftSubsequent">
    /// If true, all segments on the track that start at or after the insertion point
    /// will be shifted later by the duration of the inserted segment. This makes space for the new segment.
    /// </param>
    public void InsertSegmentAt(AudioSegment segmentToInsert, TimeSpan insertionTime, bool shiftSubsequent = true)
    {
        segmentToInsert.ParentTrack = this;
        segmentToInsert.Settings.ParentSegment = segmentToInsert;
        if (shiftSubsequent)
        {
            var insertedDuration = segmentToInsert.EffectiveDurationOnTimeline;
            var subsequentSegments = Segments
                .Where(s => s.TimelineStartTime >= insertionTime)
                .OrderBy(s => s.TimelineStartTime)
                .ToList();
            
            foreach (var subSegment in subsequentSegments)
            {
                subSegment.TimelineStartTime += insertedDuration;
            }
        }
        segmentToInsert.TimelineStartTime = insertionTime;
        Segments.Add(segmentToInsert);
        SortSegments();
        MarkDirty();
    }

    /// <summary>
    /// Sorts the internal list of segments by their <see cref="AudioSegment.TimelineStartTime"/>.
    /// This method is called automatically after adding segments.
    /// </summary>
    private void SortSegments()
    {
        Segments.Sort((s1, s2) => s1.TimelineStartTime.CompareTo(s2.TimelineStartTime));
    }

    /// <summary>
    /// Calculates the total duration of the track based on the latest ending segment.
    /// </summary>
    /// <returns>A <see cref="TimeSpan"/> representing the total duration of the track.</returns>
    public TimeSpan CalculateDuration()
    {
        return Segments.Count == 0 ? TimeSpan.Zero : Segments.Max(s => s.TimelineEndTime);
    }

    /// <summary>
    /// Renders this track's audio output for a given time range into a provided buffer.
    /// Segments are processed, and their output is mixed together, then track-level volume and pan are applied.
    /// </summary>
    /// <param name="overallStartTime">The global timeline start time for the rendering operation.</param>
    /// <param name="durationToRender">The duration of audio to render from the track.</param>
    /// <param name="outputBuffer">The span to fill with the rendered audio samples. This buffer will be cleared before rendering.</param>
    /// <param name="targetSampleRate">The desired sample rate for the output audio.</param>
    /// <param name="targetChannels">The desired number of channels for the output audio.</param>
    /// <returns>The number of samples actually written to the output buffer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if an invalid channel value is encountered during pan calculation.</exception>
    public int Render(TimeSpan overallStartTime, TimeSpan durationToRender, Span<float> outputBuffer, int targetSampleRate, int targetChannels)
    {
        if (!Settings.IsEnabled || Settings.IsMuted)
        {
            outputBuffer.Clear(); // Fill with silence
            return outputBuffer.Length;
        }

        var totalSamplesToRender = (int)(durationToRender.TotalSeconds * targetSampleRate * targetChannels);
        totalSamplesToRender = Math.Min(totalSamplesToRender, outputBuffer.Length);
        outputBuffer[..totalSamplesToRender].Clear(); // Start with silence

        var overallEndTime = overallStartTime + durationToRender;

        float[]? segmentOutputBuffer = null;

        try
        {
            foreach (var segment in Segments)
            {
                if (!segment.Settings.IsEnabled) continue;

                var segmentTimelineStart = segment.TimelineStartTime;
                var segmentTimelineEnd = segment.TimelineEndTime;

                // Check for overlap between [overallStartTime, overallEndTime] and [segmentTimelineStart, segmentTimelineEnd]
                var overlapStart = Max(overallStartTime, segmentTimelineStart);
                var overlapEnd = Min(overallEndTime, segmentTimelineEnd);

                if (overlapStart < overlapEnd) // There is an overlap
                {
                    var durationOfOverlap = overlapEnd - overlapStart;
                    var samplesInOverlap = (int)(durationOfOverlap.TotalSeconds * targetSampleRate * targetChannels);
                    if (samplesInOverlap <= 0) continue;

                    // Allocate/resize segment output buffer if needed
                    if (segmentOutputBuffer == null || segmentOutputBuffer.Length < samplesInOverlap)
                    {
                        if (segmentOutputBuffer != null) ArrayPool<float>.Shared.Return(segmentOutputBuffer);
                        segmentOutputBuffer = ArrayPool<float>.Shared.Rent(samplesInOverlap);
                    }
                    var segmentOutputSpan = segmentOutputBuffer.AsSpan(0, samplesInOverlap);

                    // Time offset within the segment to start reading from
                    var segmentTimeOffsetForRead = overlapStart - segmentTimelineStart;

                    segment.ReadProcessedSamples(segmentTimeOffsetForRead, durationOfOverlap, segmentOutputSpan, 0, targetSampleRate, targetChannels);

                    // Mix into the main outputBuffer
                    // Calculate where this overlap sits in the main outputBuffer
                    var outputBufferStartOffset = (int)((overlapStart - overallStartTime).TotalSeconds * targetSampleRate * targetChannels);

                    for (var i = 0; i < samplesInOverlap; i++)
                    {
                        if (outputBufferStartOffset + i < outputBuffer.Length)
                        {
                            outputBuffer[outputBufferStartOffset + i] += segmentOutputSpan[i]; // Mix segment output
                        }
                    }
                }
            }
        }
        finally
        {
            if (segmentOutputBuffer != null) ArrayPool<float>.Shared.Return(segmentOutputBuffer);
        }

        // Apply Track Modifiers
        foreach (var modifier in Settings.Modifiers)
        {
            if (modifier.Enabled)
            {
                modifier.Process(outputBuffer[..totalSamplesToRender], targetChannels);
            }
        }

        // Process Track Analyzers
        foreach (var analyzer in Settings.Analyzers)
        {
            if (analyzer.Enabled)
            {
                analyzer.Process(outputBuffer[..totalSamplesToRender], targetChannels);
            }
        }

        // Apply Track Volume and Pan to the final track output
        for (var i = 0; i < totalSamplesToRender; i++)
        {
            var sample = outputBuffer[i];
            sample *= Settings.Volume;

            if (targetChannels == 2)
            {
                var ch = i % targetChannels;
                var panFactor = (Settings.Pan + 1.0f) / 2.0f;
                sample *= ch == 0 ? (1.0f - panFactor) * 1.414f : panFactor * 1.414f; // Approx equal power
            }

            outputBuffer[i] = sample;
        }

        return totalSamplesToRender;
    }


    /// <summary>
    /// Compares two <see cref="TimeSpan"/> values and returns the greater one.
    /// </summary>
    /// <param name="t1">The first TimeSpan to compare.</param>
    /// <param name="t2">The second TimeSpan to compare.</param>
    /// <returns>The larger of the two TimeSpans.</returns>
    private static TimeSpan Max(TimeSpan t1, TimeSpan t2) => t1 > t2 ? t1 : t2;

    /// <summary>
    /// Compares two <see cref="TimeSpan"/> values and returns the smaller one.
    /// </summary>
    /// <param name="t1">The first TimeSpan to compare.</param>
    /// <param name="t2">The second TimeSpan to compare.</param>
    /// <returns>The smaller of the two TimeSpans.</returns>
    private static TimeSpan Min(TimeSpan t1, TimeSpan t2) => t1 < t2 ? t1 : t2;
}
