using SoundFlow.Metadata.Midi.Enums;

namespace SoundFlow.Metadata.Midi;

/// <summary>
/// Represents a parsed Standard MIDI File (SMF), containing header information and all tracks.
/// </summary>
public sealed class MidiFile
{
    /// <summary>
    /// Gets the MIDI file format type (0, 1, or 2).
    /// </summary>
    public int Format { get; internal set; }

    /// <summary>
    /// Gets the time division, specifying the number of ticks per quarter note.
    /// </summary>
    public int TicksPerQuarterNote { get; set; }

    /// <summary>
    /// Gets the list of tracks contained in the MIDI file.
    /// </summary>
    public IReadOnlyList<MidiTrack> Tracks => _tracks.AsReadOnly();
    
    private readonly List<MidiTrack> _tracks = [];

    internal void AddTrack(MidiTrack track) => _tracks.Add(track);

    /// <summary>
    /// Calculates the total number of notes in the file.
    /// This counts all Note On events with a velocity greater than 0.
    /// </summary>
    public int NumberOfNotes => Tracks.SelectMany(t => t.Events)
                                      .OfType<ChannelEvent>()
                                      .Count(e => (e.Message.StatusByte & 0xF0) == 0x90 && e.Message.Velocity > 0);

    /// <summary>
    /// Calculates the lowest and highest note numbers (pitch) present in the file.
    /// Returns null if there are no notes in the file.
    /// </summary>
    public (int Min, int Max)? NoteRange
    {
        get
        {
            var noteEvents = Tracks.SelectMany(t => t.Events)
                                   .OfType<ChannelEvent>()
                                   .Where(e => (e.Message.StatusByte & 0xF0) == 0x90 && e.Message.Velocity > 0)
                                   .Select(e => e.Message.NoteNumber)
                                   .ToList();

            if (noteEvents.Count == 0)
                return null;

            return (noteEvents.Min(), noteEvents.Max());
        }
    }

    /// <summary>
    /// Gets the initial time signature of the file.
    /// Returns null if no time signature event is found.
    /// </summary>
    public (int Numerator, int Denominator)? InitialTimeSignature
    {
        get
        {
            var timeSigEvent = Tracks.SelectMany(t => t.Events)
                                     .OfType<MetaEvent>()
                                     .FirstOrDefault(e => e.Type == MetaEventType.TimeSignature);

            if (timeSigEvent == null || timeSigEvent.Data.Length < 2)
                return null;

            var numerator = timeSigEvent.Data[0];
            var denominator = (int)Math.Pow(2, timeSigEvent.Data[1]);
            return (numerator, denominator);
        }
    }

    /// <summary>
    /// Gets the initial tempo in Beats Per Minute (BPM).
    /// Returns null if no tempo event is found.
    /// </summary>
    public double? InitialBeatsPerMinute
    {
        get
        {
            var setTempoEvent = Tracks.SelectMany(t => t.Events)
                                      .OfType<MetaEvent>()
                                      .FirstOrDefault(e => e.Type == MetaEventType.SetTempo);

            if (setTempoEvent == null || setTempoEvent.Data.Length < 3)
                return null;
            
            // The tempo is stored as a 3-byte integer representing microseconds per quarter note
            var microsecondsPerQuarterNote = (setTempoEvent.Data[0] << 16) | (setTempoEvent.Data[1] << 8) | setTempoEvent.Data[2];

            // Formula of BPM = 60,000,000 / microseconds per quarter note
            return 60_000_000.0 / microsecondsPerQuarterNote;
        }
    }
    

    /// <summary>
    /// Gets the note range as a formatted string (e.g., "A2-C6").
    /// Returns null if there are no notes in the file.
    /// </summary>
    public string? NoteRangeName
    {
        get
        {
            var range = NoteRange;
            return !range.HasValue ? null : $"{ToNoteName(range.Value.Min)}-{ToNoteName(range.Value.Max)}";
        }
    }

    /// <summary>
    /// Converts a MIDI note number into its scientific pitch notation string (e.g., 60 -> "C4").
    /// </summary>
    /// <param name="noteNumber">The MIDI note number (0-127).</param>
    /// <returns>The note name string.</returns>
    public static string ToNoteName(int noteNumber)
    {
        if (noteNumber is < 0 or > 127)
            throw new ArgumentOutOfRangeException(nameof(noteNumber), "Note number must be between 0 and 127.");

        string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        
        var note = noteNames[noteNumber % 12];
        var octave = noteNumber / 12 - 1; // Scientific Pitch Conversion

        return $"{note}{octave}";
    }
}

/// <summary>
/// Represents a single track within a MIDI file, containing a sequence of MIDI events.
/// </summary>
public sealed class MidiTrack
{
    /// <summary>
    /// Gets the list of MIDI events in this track.
    /// </summary>
    public IReadOnlyList<MidiEvent> Events => _events.AsReadOnly();
    
    private readonly List<MidiEvent> _events = [];

    internal void AddEvent(MidiEvent midiEvent) => _events.Add(midiEvent);
}