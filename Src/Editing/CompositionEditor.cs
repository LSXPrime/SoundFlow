using SoundFlow.Abstracts;
using SoundFlow.Interfaces;
using SoundFlow.Metadata.Models;
using SoundFlow.Providers;
using SoundFlow.Utils;

namespace SoundFlow.Editing;

/// <summary>
/// Provides high-level editing logic that modifies a <see cref="Composition"/> object.
/// This class encapsulates the actions that can be performed on a composition's structure.
/// </summary>
public sealed class CompositionEditor : IDisposable
{
    private readonly Composition _composition;
    private readonly AudioEngine _engine;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositionEditor"/> class.
    /// </summary>
    /// <param name="composition">The composition instance to be edited.</param>
    /// <param name="engine">The audio engine context required for operations like file loading.</param>
    internal CompositionEditor(Composition composition, AudioEngine engine)
    {
        _composition = composition;
        _engine = engine;
    }

    /// <summary>
    /// Asynchronously creates a new <see cref="AudioSegment"/> from a file path and adds it to the specified track.
    /// This helper method automatically reads the file's metadata to determine its format and duration.
    /// </summary>
    /// <param name="track">The track to add the new segment to.</param>
    /// <param name="filePath">The full path to the audio file.</param>
    /// <param name="timelineStartTime">The time on the track's timeline where the segment should start.</param>
    /// <param name="options">Optional configuration for reading metadata.</param>
    /// <returns>The newly created and added <see cref="AudioSegment"/>.</returns>
    public AudioSegment CreateAndAddSegmentFromFile(Track track, string filePath, TimeSpan timelineStartTime, ReadOptions? options = null)
    {
        var segment = CreateSegmentFromFile(filePath, timelineStartTime, options);
        track.AddSegment(segment);
        return segment;
    }

    /// <summary>
    /// Asynchronously creates a new <see cref="AudioSegment"/> from a file path.
    /// This factory method automatically reads metadata to configure the segment.
    /// The returned segment is not yet added to any track.
    /// </summary>
    /// <param name="filePath">The full path to the audio file.</param>
    /// <param name="timelineStartTime">The time on the timeline where the segment will be placed.</param>
    /// <param name="options">Optional configuration for reading metadata.</param>
    /// <returns>A new, configured <see cref="AudioSegment"/>.</returns>
    public AudioSegment CreateSegmentFromFile(string filePath, TimeSpan timelineStartTime, ReadOptions? options = null)
    {
        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
        var dataProvider = new StreamDataProvider(_engine, stream, options); // The provider will be owned by the segment

        var formatInfo = dataProvider.FormatInfo ?? throw new InvalidOperationException("Could not read format info from file.");
        var sourceDuration = formatInfo.Duration;
        var segmentName = formatInfo.Tags?.Title ?? Path.GetFileNameWithoutExtension(filePath);

        var segment = new AudioSegment(
            _composition.Format,
            dataProvider,
            TimeSpan.Zero,
            sourceDuration,
            timelineStartTime,
            segmentName,
            ownsDataProvider: true
        );

        return segment;
    }
    
    /// <summary>
    /// Adds a <see cref="Track"/> to the composition.
    /// </summary>
    /// <param name="track">The track to add.</param>
    public void AddTrack(Track track)
    {
        track.ParentComposition = _composition;
        _composition.Tracks.Add(track);
        _composition.RegisterMappableObject(track);
        _composition.RegisterMappableObject(track.Settings);
        _composition.MarkDirty();
    }
    
    /// <summary>
    /// Adds a <see cref="MidiTrack"/> to the composition.
    /// </summary>
    /// <param name="midiTrack">The MIDI track to add.</param>
    public void AddMidiTrack(MidiTrack midiTrack)
    {
        midiTrack.ParentComposition = _composition;
        _composition.MidiTracks.Add(midiTrack);
        _composition.RegisterMappableObject(midiTrack.Settings);
        _composition.MarkDirty();
    }

    /// <summary>
    /// Removes a <see cref="Track"/> from the composition.
    /// </summary>
    /// <param name="track">The track to remove.</param>
    /// <returns>True if the track was successfully removed, false otherwise.</returns>
    public bool RemoveTrack(Track track)
    {
        track.ParentComposition = null;
        var removed = _composition.Tracks.Remove(track);
        if (removed)
        {
            _composition.UnregisterMappableObject(track);
            _composition.UnregisterMappableObject(track.Settings);
            _composition.MarkDirty();
        }
        return removed;
    }

    /// <summary>
    /// Removes a <see cref="MidiTrack"/> from the composition.
    /// </summary>
    /// <param name="midiTrack">The MIDI track to remove.</param>
    /// <returns>True if the track was successfully removed, false otherwise.</returns>
    public bool RemoveMidiTrack(MidiTrack midiTrack)
    {
        midiTrack.ParentComposition = null;
        var removed = _composition.MidiTracks.Remove(midiTrack);
        if (removed)
        {
            _composition.UnregisterMappableObject(midiTrack.Settings);
            _composition.MarkDirty();
        }
        return removed;
    }

    /// <summary>
    /// Gets the tempo at a specific time in the composition.
    /// </summary>
    /// <param name="time">The timeline position to query.</param>
    /// <returns>The active TempoMarker at the given time.</returns>
    public TempoMarker GetTempoAtTime(TimeSpan time)
    {
        return _composition.TempoTrack.LastOrDefault(m => m.Time <= time);
    }

    /// <summary>
    /// Sets or updates the tempo of the composition at a specific time.
    /// </summary>
    /// <param name="time">The timeline position for the tempo change.</param>
    /// <param name="newBpm">The new tempo in beats per minute.</param>
    public void SetTempo(TimeSpan time, double newBpm)
    {
        // Remove any existing marker at the exact same time to avoid duplicates.
        _composition.TempoTrack.RemoveAll(m => m.Time == time);
        _composition.TempoTrack.Add(new TempoMarker(time, newBpm));
        _composition.TempoTrack.Sort((a, b) => a.Time.CompareTo(b.Time));
        _composition.MarkDirty();
    }

    /// <summary>
    /// Removes a tempo change marker from the composition's tempo track.
    /// The marker at time zero cannot be removed.
    /// </summary>
    /// <param name="time">The exact time of the tempo marker to remove.</param>
    /// <returns>True if a marker was found and removed, false otherwise.</returns>
    public bool RemoveTempoChange(TimeSpan time)
    {
        if (time == TimeSpan.Zero) return false; // Cannot remove the initial tempo.
        var removedCount = _composition.TempoTrack.RemoveAll(m => m.Time == time);
        if (removedCount > 0)
        {
            _composition.MarkDirty();
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Sets the MIDI resolution for the composition. This should typically be set
    /// on a new or empty project, as changing it later does not automatically scale existing MIDI data.
    /// </summary>
    /// <param name="ticksPerQuarterNote">The new time division in ticks per quarter note.</param>
    public void SetTicksPerQuarterNote(int ticksPerQuarterNote)
    {
        if (ticksPerQuarterNote <= 0)
            throw new ArgumentOutOfRangeException(nameof(ticksPerQuarterNote), "Ticks per quarter note must be positive.");
        _composition.TicksPerQuarterNote = ticksPerQuarterNote;
        _composition.MarkDirty();
    }
    
    /// <summary>
    /// Asynchronously exports the composition's MIDI data to a Standard MIDI File.
    /// </summary>
    /// <param name="filePath">The full path of the file to save.</param>
    public Task ExportMidiAsync(string filePath)
    {
        return MidiExporter.ExportAsync(_composition, filePath);
    }
    
    /// <summary>
    /// Calculates the total duration of the composition, determined by the end time of the longest audio or MIDI track.
    /// </summary>
    /// <returns>A <see cref="TimeSpan"/> representing the total duration of the composition.</returns>
    public TimeSpan CalculateTotalDuration()
    {
        var audioDuration = _composition.Tracks.Count == 0 ? TimeSpan.Zero : _composition.Tracks.Max(t => t.CalculateDuration());
        var midiDuration = _composition.MidiTracks.Count == 0 ? TimeSpan.Zero : _composition.MidiTracks.Max(t => t.CalculateDuration());

        return audioDuration > midiDuration ? audioDuration : midiDuration;
    }
    
    /// <summary>
    /// Disposes of all <see cref="AudioSegment"/>s across all tracks that own their sound data providers.
    /// </summary>
    public void Dispose()
    {
        foreach (var segment in _composition.Tracks.SelectMany(track => track.Segments))
        {
            segment.Dispose();
        }
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
        _composition.MarkDirty();
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
        if (removed) _composition.MarkDirty();
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
        if (removed) _composition.MarkDirty();
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
        var silentSamplesCount = (int)(rangeDuration.TotalSeconds * _composition.SampleRate * _composition.TargetChannels);
        silentSamplesCount = Math.Max(0, silentSamplesCount); 
        
        // Create a RawDataProvider with silence (all zeros) for the new segment
        var silentDataProvider = new RawDataProvider(new float[silentSamplesCount]);
        var mainSilentSegment = new AudioSegment(
            _composition.Format,
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
                // Before silence, Modify the original segment
                var part1TimelineDuration = rangeStartTime - segmentTimelineStart;
                var part1SourceDuration = TimeSpan.FromTicks((long)(part1TimelineDuration.Ticks * segment.Settings.SpeedFactor));
                
                // After silence, Create a new segment
                var part3TimelineStart = rangeEndTime;
                var part3OriginalTimelineOffset = rangeEndTime - segmentTimelineStart; // Offset from original segment's start on timeline
                var part3SourceOffsetFromOriginalSourceStart = TimeSpan.FromTicks((long)(part3OriginalTimelineOffset.Ticks * segment.Settings.SpeedFactor));
                var part3SourceStartTime = segment.SourceStartTime + part3SourceOffsetFromOriginalSourceStart;
                var part3SourceDuration = segment.SourceDuration - part3SourceOffsetFromOriginalSourceStart;

                if (part3SourceDuration > TimeSpan.Zero)
                {
                    var part3Segment = new AudioSegment(
                        _composition.Format,
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

        foreach (var newSeg in segmentsToAdd)
        {
            track.AddSegment(newSeg);
        }
        
        track.AddSegment(mainSilentSegment);

        _composition.MarkDirty();
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
        _composition.MarkDirty();
    }
    
    #region MIDI Editing

    /// <summary>
    /// Gets a read-only collection of all notes contained within a MIDI segment.
    /// </summary>
    /// <param name="segment">The MIDI segment to query.</param>
    /// <returns>A read-only collection of <see cref="MidiNote"/> objects.</returns>
    public IReadOnlyCollection<MidiNote> GetNotes(MidiSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return segment.Sequence.Notes;
    }

    /// <summary>
    /// Adds a new note to a MIDI segment.
    /// </summary>
    /// <param name="segment">The segment to add the note to.</param>
    /// <param name="startTick">The start time of the note in ticks, relative to the start of the segment.</param>
    /// <param name="durationTicks">The duration of the note in ticks.</param>
    /// <param name="noteNumber">The MIDI note number (0-127).</param>
    /// <param name="velocity">The note-on velocity (1-127).</param>
    public void AddNoteToSegment(MidiSegment segment, long startTick, long durationTicks, int noteNumber, int velocity)
    {
        ArgumentNullException.ThrowIfNull(segment);
        segment.Sequence.AddNote(startTick, durationTicks, noteNumber, velocity);
        segment.MarkDirty();
    }

    /// <summary>
    /// Applies a collection of modifications to notes within a MIDI segment.
    /// This is the primary method for moving, resizing, or changing the properties of multiple notes at once.
    /// </summary>
    /// <param name="segment">The segment containing the notes to modify.</param>
    /// <param name="modifications">A collection of <see cref="NoteModification"/> objects describing the changes.</param>
    public void ModifyNotesInSegment(MidiSegment segment, IEnumerable<NoteModification> modifications)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(modifications);
        segment.Sequence.ModifyNotes(modifications);
        segment.MarkDirty();
    }

    /// <summary>
    /// Removes a collection of notes from a MIDI segment, identified by their unique IDs.
    /// </summary>
    /// <param name="segment">The segment to remove notes from.</param>
    /// <param name="noteIds">A collection of unique IDs of the notes to be removed.</param>
    public void RemoveNotesFromSegment(MidiSegment segment, IEnumerable<Guid> noteIds)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(noteIds);
        segment.Sequence.RemoveNotes(noteIds);
        segment.MarkDirty();
    }

    /// <summary>
    /// Gets a read-only collection of automation control points for a specific controller from a MIDI segment.
    /// </summary>
    /// <param name="segment">The MIDI segment to query.</param>
    /// <param name="controllerNumber">The controller number (e.g., 7 for Volume, 64 for Sustain). Use -1 for Pitch Bend.</param>
    /// <returns>A read-only collection of <see cref="ControlPoint"/> objects, or an empty collection if none exist.</returns>
    public IReadOnlyCollection<ControlPoint> GetControlPoints(MidiSegment segment, int controllerNumber)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (controllerNumber == -1)
        {
            return segment.Sequence.PitchBendEvents;
        }
        return segment.Sequence.ControlChangeEvents.TryGetValue(controllerNumber, out var points) ? points : [];
    }
    
    /// <summary>
    /// Adds a new automation control point to a MIDI segment.
    /// </summary>
    /// <param name="segment">The segment to add the control point to.</param>
    /// <param name="controllerNumber">The CC number (0-127), or -1 for Pitch Bend.</param>
    /// <param name="tick">The absolute time in ticks from the start of the segment.</param>
    /// <param name="value">The event value (0-127 for CC, 0-16383 for Pitch Bend).</param>
    public void AddControlPointToSegment(MidiSegment segment, int controllerNumber, long tick, int value)
    {
        ArgumentNullException.ThrowIfNull(segment);
        segment.Sequence.AddControlPoint(controllerNumber, tick, value);
        segment.MarkDirty();
    }
    
    /// <summary>
    /// Applies a collection of modifications to control points within a MIDI segment.
    /// </summary>
    /// <param name="segment">The segment containing the control points to modify.</param>
    /// <param name="controllerNumber">The CC number (0-127), or -1 for Pitch Bend.</param>
    /// <param name="modifications">A collection of <see cref="ControlPointModification"/> objects describing the changes.</param>
    public void ModifyControlPointsInSegment(MidiSegment segment, int controllerNumber, IEnumerable<ControlPointModification> modifications)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(modifications);
        segment.Sequence.ModifyControlPoints(controllerNumber, modifications);
        segment.MarkDirty();
    }
    
    /// <summary>
    /// Removes a collection of control points from a MIDI segment.
    /// </summary>
    /// <param name="segment">The segment to remove control points from.</param>
    /// <param name="controllerNumber">The CC number (0-127), or -1 for Pitch Bend.</param>
    /// <param name="pointIds">A collection of unique IDs of the control points to be removed.</param>
    public void RemoveControlPointsFromSegment(MidiSegment segment, int controllerNumber, IEnumerable<Guid> pointIds)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(pointIds);
        segment.Sequence.RemoveControlPoints(controllerNumber, pointIds);
        segment.MarkDirty();
    }

    /// <summary>
    /// Quantizes the notes within a MIDI segment according to the specified settings.
    /// </summary>
    /// <param name="segment">The MIDI segment to quantize.</param>
    /// <param name="settings">The quantization settings to apply.</param>
    public void QuantizeSegment(MidiSegment segment, QuantizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(settings);

        var modifications = MidiQuantizer.CalculateQuantization(segment.Sequence.Notes, settings, segment.Sequence.TicksPerQuarterNote);
        segment.Sequence.ModifyNotes(modifications);
        
        segment.MarkDirty();
    }
    
    /// <summary>
    /// Splits a MIDI segment into two at a specified timeline position.
    /// </summary>
    /// <param name="track">The track containing the segment to split.</param>
    /// <param name="segmentToSplit">The segment to split.</param>
    /// <param name="splitTimeOnTimeline">The absolute timeline time at which to split the segment.</param>
    /// <returns>A tuple containing the two new segments created by the split, or null if the split was not possible.</returns>
    public (MidiSegment? part1, MidiSegment? part2)? SplitMidiSegment(MidiTrack track, MidiSegment segmentToSplit, TimeSpan splitTimeOnTimeline)
    {
        var splitTick = MidiTimeConverter.GetTickForTimeSpan(splitTimeOnTimeline - segmentToSplit.TimelineStartTime, segmentToSplit.Sequence.TicksPerQuarterNote, _composition.TempoTrack);
        if (splitTick <= 0 || splitTick >= segmentToSplit.Sequence.LengthTicks) return null;

        var (seq1, seq2) = segmentToSplit.Sequence.Split(splitTick);

        var seg1 = new MidiSegment(seq1, segmentToSplit.TimelineStartTime, $"{segmentToSplit.Name} (1)");
        var seg2 = new MidiSegment(seq2, splitTimeOnTimeline, $"{segmentToSplit.Name} (2)");

        track.RemoveSegment(segmentToSplit);
        track.AddSegment(seg1);
        track.AddSegment(seg2);

        _composition.MarkDirty();
        return (seg1, seg2);
    }
    
    /// <summary>
    /// Joins a collection of MIDI segments on a track into a single new segment.
    /// The segments must be on the same track. They will be sorted by time before joining.
    /// </summary>
    /// <param name="track">The track containing the segments.</param>
    /// <param name="segmentsToJoin">Enumerable of segments to join.</param>
    /// <returns>The new, merged MIDI segment, or null if the operation failed.</returns>
    public MidiSegment? JoinMidiSegments(MidiTrack track, IEnumerable<MidiSegment> segmentsToJoin)
    {
        var orderedSegments = segmentsToJoin.OrderBy(s => s.TimelineStartTime).ToList();
        if (orderedSegments.Count < 2) return null;

        var firstSegment = orderedSegments[0];
        var referenceTime = firstSegment.TimelineStartTime;
        var sequencesWithTickOffsets = new List<(long tickOffset, MidiSequence sequence)>();

        foreach (var segment in orderedSegments)
        {
            var relativeTimeOffset = segment.TimelineStartTime - referenceTime;
            var tickOffset = MidiTimeConverter.GetTickForTimeSpan(
                relativeTimeOffset, 
                _composition.TicksPerQuarterNote, 
                _composition.TempoTrack);

            sequencesWithTickOffsets.Add((tickOffset, segment.Sequence));
        }

        var joinedSequence = MidiSequence.Join(sequencesWithTickOffsets);
        
        var newSegment = new MidiSegment(joinedSequence, referenceTime, "Joined Segment");
        
        foreach (var segment in orderedSegments)
        {
            track.RemoveSegment(segment);
        }
        track.AddSegment(newSegment);
        
        _composition.MarkDirty();
        return newSegment;
    }

    #endregion
}