using SoundFlow.Metadata.Midi;
using SoundFlow.Metadata.Midi.Enums;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Structs;

namespace SoundFlow.Editing;

/// <summary>
/// A mutable container for all MIDI data within a segment, designed for editing.
/// It synchronizes a high-level list of <see cref="MidiNote"/> objects with a low-level,
/// time-sorted list of raw <see cref="MidiEvent"/>s.
/// </summary>
public class MidiSequence
{
    private readonly List<MidiEvent> _otherEvents = []; // For meta, sysex events
    private readonly Dictionary<Guid, MidiNote> _notes = new();
    private readonly Dictionary<Guid, ControlPoint> _pitchBendEvents = new();
    private readonly Dictionary<int, Dictionary<Guid, ControlPoint>> _controlChangeEvents = new();
    private bool _isDirty = true;
    private IReadOnlyList<MidiEvent>? _eventCache;

    /// <summary>
    /// Gets the time division for this sequence, in ticks per quarter note.
    /// </summary>
    public int TicksPerQuarterNote { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiSequence"/> class from a <see cref="MidiFile"/>.
    /// </summary>
    /// <param name="midiFile">The MIDI file to load data from.</param>
    public MidiSequence(MidiFile midiFile)
    {
        TicksPerQuarterNote = midiFile.TicksPerQuarterNote;
        BuildFromMidiFile(midiFile);
    }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="MidiSequence"/> class from collections of editable MIDI objects.
    /// </summary>
    public MidiSequence(
        int ticksPerQuarterNote,
        IEnumerable<MidiNote> notes,
        IEnumerable<(int ccNumber, ControlPoint point)> ccEvents,
        IEnumerable<ControlPoint> pitchBendEvents,
        IEnumerable<MidiEvent> otherEvents)
    {
        TicksPerQuarterNote = ticksPerQuarterNote;
        foreach (var note in notes) _notes.Add(note.Id, note);
        foreach (var pb in pitchBendEvents) _pitchBendEvents.Add(pb.Id, pb);
        foreach (var (cc, point) in ccEvents)
        {
            if (!_controlChangeEvents.TryGetValue(cc, out var dict))
            {
                dict = [];
                _controlChangeEvents[cc] = dict;
            }
            dict.Add(point.Id, point);
        }
        _otherEvents.AddRange(otherEvents);
        _isDirty = true;
    }

    /// <summary>
    /// Gets a read-only collection of all notes in the sequence.
    /// </summary>
    public IReadOnlyCollection<MidiNote> Notes => _notes.Values;
    
    /// <summary>
    /// Gets a read-only list of all raw MIDI events, sorted by time.
    /// The `DeltaTimeTicks` property in this context stores the absolute tick position.
    /// </summary>
    public IReadOnlyList<MidiEvent> Events
    {
        get
        {
            if (_isDirty || _eventCache == null)
            {
                RebuildEventCache();
            }
            return _eventCache!;
        }
    }
    
    /// <summary>
    /// Gets the total length of the sequence in ticks, determined by the last event.
    /// </summary>
    public long LengthTicks => Events.Count > 0 ? Events[^1].DeltaTimeTicks : 0;
    
    /// <summary>
    /// Gets a read-only collection of pitch bend events.
    /// </summary>
    public IReadOnlyCollection<ControlPoint> PitchBendEvents => _pitchBendEvents.Values;
    
    /// <summary>
    /// Gets a read-only dictionary of Control Change events, keyed by CC number.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyCollection<ControlPoint>> ControlChangeEvents => 
        _controlChangeEvents.ToDictionary(kvp => kvp.Key, IReadOnlyCollection<ControlPoint> (kvp) => kvp.Value.Values);

    /// <summary>
    /// Converts the current state of the sequence into a playable <see cref="MidiFile"/> object with correct delta-times.
    /// </summary>
    /// <returns>A new <see cref="MidiFile"/> instance.</returns>
    public MidiFile ToMidiFile()
    {
        var sortedEvents = Events; // This will trigger a rebuild if dirty
        
        var midiFile = new MidiFile { TicksPerQuarterNote = TicksPerQuarterNote, Format = 1 };
        var track = new Metadata.Midi.MidiTrack();
        
        long lastTick = 0;
        foreach (var absoluteEvent in sortedEvents)
        {
            var delta = absoluteEvent.DeltaTimeTicks - lastTick;
            track.AddEvent(absoluteEvent with { DeltaTimeTicks = delta });
            lastTick = absoluteEvent.DeltaTimeTicks;
        }

        // Ensure there's an EndOfTrack event.
        if (track.Events.LastOrDefault() is not MetaEvent { Type: MetaEventType.EndOfTrack })
        {
            track.AddEvent(new MetaEvent(0, MetaEventType.EndOfTrack, []));
        }

        midiFile.AddTrack(track);
        return midiFile;
    }

    /// <summary>
    /// Adds a new note to the sequence.
    /// </summary>
    /// <param name="startTick">The absolute start time of the note in ticks.</param>
    /// <param name="durationTicks">The duration of the note in ticks.</param>
    /// <param name="noteNumber">The MIDI note number (0-127).</param>
    /// <param name="velocity">The note-on velocity (1-127).</param>
    /// <returns>The newly created <see cref="MidiNote"/> object.</returns>
    public MidiNote AddNote(long startTick, long durationTicks, int noteNumber, int velocity)
    {
        var note = new MidiNote(startTick, durationTicks, noteNumber, velocity);
        _notes.Add(note.Id, note);
        _isDirty = true;
        return note;
    }

    /// <summary>
    /// Removes one or more notes from the sequence, identified by their unique IDs.
    /// </summary>
    /// <param name="noteIds">An enumerable collection of note IDs to remove.</param>
    public void RemoveNotes(IEnumerable<Guid> noteIds)
    {
        var changed = false;
        foreach (var id in noteIds)
        {
            if (_notes.Remove(id)) changed = true;
        }
        if (changed) _isDirty = true;
    }
    
    /// <summary>
    /// Applies a batch of modifications to notes in the sequence. This is the primary method for note editing.
    /// </summary>
    /// <param name="modifications">An enumerable collection of <see cref="NoteModification"/> objects.</param>
    public void ModifyNotes(IEnumerable<NoteModification> modifications)
    {
        var changed = false;
        foreach (var mod in modifications)
        {
            if (!_notes.TryGetValue(mod.NoteId, out var note))
                continue;

            note.StartTick = mod.NewStartTick ?? note.StartTick;
            note.DurationTicks = mod.NewDurationTicks ?? note.DurationTicks;
            note.NoteNumber = mod.NewNoteNumber ?? note.NoteNumber;
            note.Velocity = mod.NewVelocity ?? note.Velocity;
            changed = true;
        }
        if (changed) _isDirty = true;
    }
    
    /// <summary>
    /// Adds a Control Change or Pitch Bend event to the sequence.
    /// </summary>
    /// <param name="controllerNumber">The CC number (0-127), or -1 for Pitch Bend.</param>
    /// <param name="tick">The absolute time in ticks.</param>
    /// <param name="value">The event value (0-127 for CC, 0-16383 for Pitch Bend).</param>
    /// <returns>The newly created <see cref="ControlPoint"/>.</returns>
    public ControlPoint AddControlPoint(int controllerNumber, long tick, int value)
    {
        var point = new ControlPoint(tick, value);
        if (controllerNumber == -1) // Pitch Bend
        {
            _pitchBendEvents.Add(point.Id, point);
        }
        else
        {
            if (!_controlChangeEvents.TryGetValue(controllerNumber, out var dict))
            {
                dict = [];
                _controlChangeEvents[controllerNumber] = dict;
            }
            dict.Add(point.Id, point);
        }
        _isDirty = true;
        return point;
    }
    
    /// <summary>
    /// Removes one or more control points from the sequence.
    /// </summary>
    /// <param name="controllerNumber">The CC number (0-127), or -1 for Pitch Bend.</param>
    /// <param name="pointIds">An enumerable collection of control point IDs to remove.</param>
    public void RemoveControlPoints(int controllerNumber, IEnumerable<Guid> pointIds)
    {
        var changed = false;
        if (controllerNumber == -1) // Pitch Bend
        {
            foreach (var id in pointIds)
            {
                if (_pitchBendEvents.Remove(id)) changed = true;
            }
        }
        else if (_controlChangeEvents.TryGetValue(controllerNumber, out var dict))
        {
            foreach (var id in pointIds)
            {
                if (dict.Remove(id)) changed = true;
            }
        }
        if (changed) _isDirty = true;
    }

    /// <summary>
    /// Applies a batch of modifications to control points in the sequence.
    /// </summary>
    /// <param name="controllerNumber">The CC number (0-127), or -1 for Pitch Bend.</param>
    /// <param name="modifications">An enumerable collection of <see cref="ControlPointModification"/> objects.</param>
    public void ModifyControlPoints(int controllerNumber, IEnumerable<ControlPointModification> modifications)
    {
        var changed = false;
        Dictionary<Guid, ControlPoint>? dict;
        if (controllerNumber == -1) dict = _pitchBendEvents;
        else _controlChangeEvents.TryGetValue(controllerNumber, out dict);

        if (dict == null) return;
        
        foreach (var mod in modifications)
        {
            if (!dict.TryGetValue(mod.PointId, out var point)) continue;
            
            point.Tick = mod.NewTick ?? point.Tick;
            point.Value = mod.NewValue ?? point.Value;
            changed = true;
        }
        if (changed) _isDirty = true;
    }
    
    /// <summary>
    /// Rebuilds the internal, time-sorted cache of all MIDI events from the high-level editable collections.
    /// </summary>
    private void RebuildEventCache()
    {
        var events = new List<MidiEvent>(_otherEvents);

        // Add Note On/Off events from MidiNote objects
        foreach (var note in _notes.Values)
        {
            events.Add(new ChannelEvent(note.StartTick, new MidiMessage((int)MidiCommand.NoteOn, (byte)note.NoteNumber, (byte)note.Velocity)));
            events.Add(new ChannelEvent(note.StartTick + note.DurationTicks, new MidiMessage((int)MidiCommand.NoteOff, (byte)note.NoteNumber, 0)));
        }

        // Add Pitch Bend events
        foreach (var pb in _pitchBendEvents.Values)
        {
            var lsb = (byte)(pb.Value & 0x7F);
            var msb = (byte)(pb.Value >> 7);
            events.Add(new ChannelEvent(pb.Tick, new MidiMessage((int)MidiCommand.PitchBend, lsb, msb)));
        }

        // Add Control Change events
        foreach (var (ccNumber, points) in _controlChangeEvents)
        {
            events.AddRange(points.Values.Select(point => new ChannelEvent(point.Tick, new MidiMessage((int)MidiCommand.ControlChange, (byte)ccNumber, (byte)point.Value))));
        }
        
        events.Sort((a, b) => a.DeltaTimeTicks.CompareTo(b.DeltaTimeTicks));
        _eventCache = events.AsReadOnly();
        _isDirty = false;
    }
    
    /// <summary>
    /// Populates the sequence from a MidiFile, converting all delta-times to absolute ticks
    /// and building the high-level editable data structures.
    /// </summary>
    private void BuildFromMidiFile(MidiFile midiFile)
    {
        var openNotes = new Dictionary<(int channel, int pitch), (long startTick, int velocity)>();
        
        var absoluteEvents = new List<(long tick, MidiEvent evt)>();
        foreach (var track in midiFile.Tracks)
        {
            long absoluteTime = 0;
            foreach (var e in track.Events)
            {
                absoluteTime += e.DeltaTimeTicks;
                absoluteEvents.Add((absoluteTime, e));
            }
        }
        absoluteEvents.Sort((a, b) => a.tick.CompareTo(b.tick));

        foreach (var (absoluteTime, midiEvent) in absoluteEvents)
        {
            if (midiEvent is not ChannelEvent ce)
            {
                // Store non-channel events directly
                _otherEvents.Add(midiEvent with { DeltaTimeTicks = absoluteTime });
                continue;
            }

            var key = (ce.Message.Channel, ce.Message.NoteNumber);
            switch (ce.Message.Command)
            {
                case MidiCommand.NoteOn when ce.Message.Velocity > 0:
                    openNotes[key] = (absoluteTime, ce.Message.Velocity);
                    break;
                case MidiCommand.NoteOff:
                case MidiCommand.NoteOn when ce.Message.Velocity == 0:
                    if (openNotes.Remove(key, out var noteStart))
                    {
                        AddNote(noteStart.startTick, absoluteTime - noteStart.startTick, key.NoteNumber, noteStart.velocity);
                    }
                    break;
                case MidiCommand.PitchBend:
                    AddControlPoint(-1, absoluteTime, ce.Message.PitchBendValue);
                    break;
                case MidiCommand.ControlChange:
                    AddControlPoint(ce.Message.ControllerNumber, absoluteTime, ce.Message.ControllerValue);
                    break;
            }
        }
        _isDirty = true;
    }

    /// <summary>
    /// Splits this sequence into two new sequences at a specific tick.
    /// Notes and events that cross the split point are handled appropriately.
    /// </summary>
    /// <param name="splitTick">The absolute tick at which to split the sequence.</param>
    /// <returns>A tuple containing the two new <see cref="MidiSequence"/> objects.</returns>
    public (MidiSequence part1, MidiSequence part2) Split(long splitTick)
    {
        var notes1 = new List<MidiNote>();
        var notes2 = new List<MidiNote>();
        var cc1 = new List<(int, ControlPoint)>();
        var cc2 = new List<(int, ControlPoint)>();
        var pb1 = new List<ControlPoint>();
        var pb2 = new List<ControlPoint>();
        var other1 = _otherEvents.Where(e => e.DeltaTimeTicks < splitTick);
        var other2 = _otherEvents.Where(e => e.DeltaTimeTicks >= splitTick).Select(e => e with { DeltaTimeTicks = e.DeltaTimeTicks - splitTick });

        foreach (var note in _notes.Values)
        {
            if (note.StartTick + note.DurationTicks <= splitTick) // Note ends before split
            {
                notes1.Add(note);
            }
            else if (note.StartTick >= splitTick) // Note starts after split
            {
                notes2.Add(new MidiNote(note.StartTick - splitTick, note.DurationTicks, note.NoteNumber, note.Velocity));
            }
            else // Note crosses the split point
            {
                notes1.Add(new MidiNote(note.StartTick, splitTick - note.StartTick, note.NoteNumber, note.Velocity));
                notes2.Add(new MidiNote(0, note.DurationTicks - (splitTick - note.StartTick), note.NoteNumber, note.Velocity));
            }
        }
        
        foreach (var pb in _pitchBendEvents.Values)
        {
            if (pb.Tick < splitTick) pb1.Add(pb); else pb2.Add(new ControlPoint(pb.Tick - splitTick, pb.Value));
        }
        
        foreach (var (cc, points) in _controlChangeEvents)
        {
            foreach (var p in points.Values)
            {
                if (p.Tick < splitTick) cc1.Add((cc, p)); else cc2.Add((cc, new ControlPoint(p.Tick - splitTick, p.Value)));
            }
        }

        var seq1 = new MidiSequence(TicksPerQuarterNote, notes1, cc1, pb1, other1);
        var seq2 = new MidiSequence(TicksPerQuarterNote, notes2, cc2, pb2, other2);
        return (seq1, seq2);
    }
    
    /// <summary>
    /// Creates a new MidiSequence by joining multiple sequences together, offsetting their timings.
    /// </summary>
    public static MidiSequence Join(IEnumerable<(long tickOffset, MidiSequence sequence)> sequences)
    {
        var ordered = sequences.OrderBy(s => s.tickOffset).ToList();
        if (ordered.Count == 0) return new MidiSequence(new MidiFile { TicksPerQuarterNote = 480 });

        var firstSeq = ordered.First().sequence;
        var ticksPerQn = firstSeq.TicksPerQuarterNote;
        
        var combinedNotes = new List<MidiNote>();
        var combinedCCs = new List<(int, ControlPoint)>();
        var combinedPBs = new List<ControlPoint>();
        var combinedOthers = new List<MidiEvent>();

        foreach (var (tickOffset, seq) in ordered)
        {
            combinedNotes.AddRange(seq.Notes.Select(n => new MidiNote(n.StartTick + tickOffset, n.DurationTicks, n.NoteNumber, n.Velocity)));
            combinedPBs.AddRange(seq.PitchBendEvents.Select(p => new ControlPoint(p.Tick + tickOffset, p.Value)));
            foreach (var (cc, points) in seq.ControlChangeEvents)
            {
                combinedCCs.AddRange(points.Select(p => (cc, new ControlPoint(p.Tick + tickOffset, p.Value))));
            }
            combinedOthers.AddRange(seq._otherEvents.Select(e => e with { DeltaTimeTicks = e.DeltaTimeTicks + tickOffset }));
        }

        return new MidiSequence(ticksPerQn, combinedNotes, combinedCCs, combinedPBs, combinedOthers);
    }
}