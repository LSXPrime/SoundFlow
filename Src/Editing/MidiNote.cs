namespace SoundFlow.Editing;

/// <summary>
/// Represents a single, editable MIDI note, providing a more intuitive object for manipulation
/// than raw Note On/Note Off events.
/// </summary>
public sealed class MidiNote
{
    /// <summary>
    /// Gets a unique identifier for this note instance.
    /// This is used to reliably track the note during editing operations.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the absolute start time of the note in MIDI ticks from the beginning of the sequence.
    /// </summary>
    public long StartTick { get; set; }

    /// <summary>
    /// Gets or sets the duration of the note in MIDI ticks.
    /// </summary>
    public long DurationTicks { get; set; }

    /// <summary>
    /// Gets or sets the MIDI note number of the note, where 60 is Middle C.
    /// </summary>
    public int NoteNumber { get; set; }

    /// <summary>
    /// Gets or sets the velocity of the Note On event (1-127).
    /// </summary>
    public int Velocity { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiNote"/> class.
    /// </summary>
    /// <param name="startTick">The absolute start time in ticks.</param>
    /// <param name="durationTicks">The duration in ticks.</param>
    /// <param name="noteNumber">The MIDI note number.</param>
    /// <param name="velocity">The note-on velocity.</param>
    public MidiNote(long startTick, long durationTicks, int noteNumber, int velocity)
    {
        StartTick = startTick;
        DurationTicks = durationTicks;
        NoteNumber = noteNumber;
        Velocity = velocity;
    }
}

/// <summary>
/// A data structure describing a modification to be applied to a <see cref="MidiNote"/>.
/// Null properties indicate no change for that attribute.
/// </summary>
public record struct NoteModification
{
    /// <summary>
    /// The unique identifier of the note to modify.
    /// </summary>
    public Guid NoteId { get; init; }

    /// <summary>
    /// The new absolute start time in ticks, if it is to be changed.
    /// </summary>
    public long? NewStartTick { get; init; }

    /// <summary>
    /// The new duration in ticks, if it is to be changed.
    /// </summary>
    public long? NewDurationTicks { get; init; }

    /// <summary>
    /// The new note number (MIDI note number), if it is to be changed.
    /// </summary>
    public int? NewNoteNumber { get; init; }

    /// <summary>
    /// The new velocity, if it is to be changed.
    /// </summary>
    public int? NewVelocity { get; init; }
}