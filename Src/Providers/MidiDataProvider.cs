using SoundFlow.Editing;
using SoundFlow.Metadata.Midi;
using SoundFlow.Metadata.Midi.Enums;
using MidiTrack = SoundFlow.Metadata.Midi.MidiTrack;

namespace SoundFlow.Providers;

/// <summary>
/// A data provider that processes a <see cref="MidiFile"/> into a single, time-ordered sequence of events,
/// and provides utilities to convert MIDI ticks into real time based on the file's internal tempo map.
/// </summary>
public sealed class MidiDataProvider
{
    /// <summary>
    /// Represents a MIDI event with an absolute time in ticks from the start of the sequence.
    /// </summary>
    public readonly record struct TimedMidiEvent(long AbsoluteTimeTicks, MidiEvent Event);

    /// <summary>
    /// Represents a tempo change at a specific point in the MIDI sequence.
    /// </summary>
    private readonly record struct TempoPoint(long AbsoluteTimeTicks, int MicrosecondsPerQuarterNote);

    private readonly List<TempoPoint> _tempoMap = [];

    /// <summary>
    /// Gets the list of all MIDI events from all tracks, sorted chronologically.
    /// </summary>
    public IReadOnlyList<TimedMidiEvent> Events { get; }

    /// <summary>
    /// Gets the time division from the MIDI file, in ticks per quarter note.
    /// </summary>
    public int TicksPerQuarterNote { get; }

    /// <summary>
    /// Gets the total duration of the MIDI sequence in ticks.
    /// </summary>
    public long LengthTicks { get; }

    /// <summary>
    /// Gets the total duration of the MIDI sequence as a TimeSpan, calculated from its internal tempo map.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiDataProvider"/> class from a <see cref="MidiFile"/>.
    /// </summary>
    /// <param name="midiFile">The parsed MIDI file to process.</param>
    public MidiDataProvider(MidiFile midiFile)
    {
        TicksPerQuarterNote = midiFile.TicksPerQuarterNote;

        var mergedEvents = new List<TimedMidiEvent>();

        // Merge events from all tracks and calculate absolute times
        long maxAbsoluteTime = 0;
        foreach (var track in midiFile.Tracks)
        {
            long absoluteTime = 0;
            foreach (var e in track.Events)
            {
                absoluteTime += e.DeltaTimeTicks;
                mergedEvents.Add(new TimedMidiEvent(absoluteTime, e));

                // Track the maximum absolute time across all tracks
                if (absoluteTime > maxAbsoluteTime) maxAbsoluteTime = absoluteTime;
            }
        }

        // Sort the merged events by their absolute time
        mergedEvents.Sort((a, b) => a.AbsoluteTimeTicks.CompareTo(b.AbsoluteTimeTicks));
        Events = mergedEvents.AsReadOnly();

        // Build the tempo map for time conversion
        BuildTempoMap();

        // Use the maximum absolute time found across all tracks as the length
        LengthTicks = maxAbsoluteTime;

        // Calculate duration using the internal tempo map
        Duration = GetTimeSpanForTick(LengthTicks);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiDataProvider"/> class from a MIDI file stream.
    /// </summary>
    /// <param name="stream">The stream containing the MIDI file data.</param>
    public MidiDataProvider(Stream stream) : this(MidiFileParser.Parse(stream))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiDataProvider"/> class from a mutable <see cref="MidiSequence"/>.
    /// This acts as a read-only "view" for the playback engine.
    /// </summary>
    /// <param name="sequence">The MIDI sequence to create a provider from.</param>
    public MidiDataProvider(MidiSequence sequence)
    {
        TicksPerQuarterNote = sequence.TicksPerQuarterNote;
        Events = sequence.Events
            .Select(absoluteEvent => new TimedMidiEvent(absoluteEvent.DeltaTimeTicks, absoluteEvent)).ToList()
            .AsReadOnly();

        BuildTempoMap();
        LengthTicks = sequence.Events.Count > 0 ? sequence.Events.Max(e => e.DeltaTimeTicks) : 0;
        Duration = GetTimeSpanForTick(LengthTicks);
    }

    /// <summary>
    /// Retrieves all MIDI events that occur within a specified time range, defined by ticks.
    /// </summary>
    /// <param name="startTick">The starting tick of the range (inclusive).</param>
    /// <param name="endTick">The ending tick of the range (exclusive).</param>
    /// <returns>An enumerable collection of timed MIDI events within the specified range.</returns>
    public IEnumerable<TimedMidiEvent> GetEvents(long startTick, long endTick)
    {
        // Find the index of the first event at or after startTick.
        var startIndex = FindFirstIndexOnOrAfter(startTick);

        if (startIndex == -1) // No events in the range
            yield break;

        // Yield events until we go past the endTick
        for (var i = startIndex; i < Events.Count; i++)
        {
            var timedEvent = Events[i];
            if (timedEvent.AbsoluteTimeTicks < endTick)
                yield return timedEvent;
            else
                // Since the list is sorted, we can stop here.
                break;
        }
    }

    // Helper method to find the starting index using binary search.
    private int FindFirstIndexOnOrAfter(long tick)
    {
        var low = 0;
        var high = Events.Count - 1;
        var result = -1;

        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (Events[mid].AbsoluteTimeTicks >= tick)
            {
                result = mid;
                high = mid - 1; // Try to find an earlier one
            }
            else
            {
                low = mid + 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Converts an absolute time in MIDI ticks to a <see cref="TimeSpan"/> using the file's internal tempo map.
    /// </summary>
    /// <param name="tick">The absolute tick position to convert.</param>
    /// <returns>The corresponding TimeSpan from the beginning of the sequence.</returns>
    public TimeSpan GetTimeSpanForTick(long tick)
    {
        if (tick <= 0) return TimeSpan.Zero;

        double timeInSeconds = 0;
        long lastTick = 0;
        var currentTempo = 500000; // Default MIDI tempo: 120 BPM

        foreach (var tempoPoint in _tempoMap)
        {
            if (tick <= tempoPoint.AbsoluteTimeTicks)
            {
                // Calculate time for the remaining segment
                var ticksInSegment = tick - lastTick;
                timeInSeconds += (double)ticksInSegment / TicksPerQuarterNote * (currentTempo / 1000000.0);
                return TimeSpan.FromSeconds(timeInSeconds);
            }

            // Calculate time for this complete segment
            var segmentTicks = tempoPoint.AbsoluteTimeTicks - lastTick;
            timeInSeconds += (double)segmentTicks / TicksPerQuarterNote * (currentTempo / 1000000.0);

            lastTick = tempoPoint.AbsoluteTimeTicks;
            currentTempo = tempoPoint.MicrosecondsPerQuarterNote;
        }

        // If we get here, the tick is beyond the last tempo change
        var remainingTicks = tick - lastTick;
        timeInSeconds += (double)remainingTicks / TicksPerQuarterNote * (currentTempo / 1000000.0);

        return TimeSpan.FromSeconds(timeInSeconds);
    }

    /// <summary>
    /// Converts a <see cref="TimeSpan"/> to an absolute time in MIDI ticks using the file's internal tempo map.
    /// </summary>
    /// <param name="time">The TimeSpan to convert.</param>
    /// <returns>The corresponding absolute tick position.</returns>
    public long GetTickForTimeSpan(TimeSpan time)
    {
        if (time <= TimeSpan.Zero) return 0;

        var timeInSeconds = time.TotalSeconds;
        long totalTicks = 0;
        double accumulatedTime = 0;
        var currentTempo = 500000; // Default MIDI tempo: 120 BPM
        long lastTick = 0;

        foreach (var tempoPoint in _tempoMap)
        {
            var secondsPerTick = currentTempo / 1000000.0 / TicksPerQuarterNote;
            var ticksToNextPoint = tempoPoint.AbsoluteTimeTicks - lastTick;
            var timeToNextPoint = ticksToNextPoint * secondsPerTick;

            if (accumulatedTime + timeToNextPoint >= timeInSeconds)
            {
                var remainingTime = timeInSeconds - accumulatedTime;
                totalTicks = lastTick + (long)(remainingTime / secondsPerTick);
                return totalTicks;
            }

            accumulatedTime += timeToNextPoint;
            totalTicks = tempoPoint.AbsoluteTimeTicks;
            currentTempo = tempoPoint.MicrosecondsPerQuarterNote;
            lastTick = tempoPoint.AbsoluteTimeTicks;
        }

        // If time is beyond the last tempo change
        var secondsPerTickFinal = currentTempo / 1000000.0 / TicksPerQuarterNote;
        var timeAfterLastPoint = timeInSeconds - accumulatedTime;
        totalTicks += (long)(timeAfterLastPoint / secondsPerTickFinal);

        return totalTicks;
    }
    
    /// <summary>
    /// Converts the data provider's event stream back into a standard MidiFile object.
    /// </summary>
    /// <returns>A new <see cref="MidiFile"/> instance.</returns>
    public MidiFile ToMidiFile()
    {
        var midiFile = new MidiFile { TicksPerQuarterNote = TicksPerQuarterNote, Format = 1 };
        var track = new MidiTrack();

        long lastTick = 0;
        foreach (var timedEvent in Events)
        {
            var delta = timedEvent.AbsoluteTimeTicks - lastTick;
            track.AddEvent(timedEvent.Event with { DeltaTimeTicks = delta });
            lastTick = timedEvent.AbsoluteTimeTicks;
        }

        // Ensure there's an EndOfTrack event.
        if (track.Events.LastOrDefault() is not MetaEvent { Type: MetaEventType.EndOfTrack })
        {
            track.AddEvent(new MetaEvent(0, MetaEventType.EndOfTrack, []));
        }

        midiFile.AddTrack(track);
        return midiFile;
    }

    private void BuildTempoMap()
    {
        // Add default tempo at the beginning
        _tempoMap.Add(new TempoPoint(0, 500000));

        foreach (var timedEvent in Events)
        {
            if (timedEvent.Event is MetaEvent { Type: MetaEventType.SetTempo, Data.Length: 3 } metaEvent)
            {
                var tempo = (metaEvent.Data[0] << 16) | (metaEvent.Data[1] << 8) | metaEvent.Data[2];
                _tempoMap.Add(new TempoPoint(timedEvent.AbsoluteTimeTicks, tempo));
            }
        }

        // Sort by time and remove duplicates, keeping the last one at a given time
        _tempoMap.Sort((a, b) => a.AbsoluteTimeTicks.CompareTo(b.AbsoluteTimeTicks));

        // Remove duplicate tempo points at the same time, keeping only the last one
        for (var i = _tempoMap.Count - 1; i > 0; i--)
        {
            if (_tempoMap[i].AbsoluteTimeTicks == _tempoMap[i - 1].AbsoluteTimeTicks)
            {
                _tempoMap.RemoveAt(i - 1);
            }
        }
    }
}