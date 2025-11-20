namespace SoundFlow.Editing;

/// <summary>
/// Defines the settings for a MIDI quantization operation.
/// </summary>
public record QuantizationSettings
{
    /// <summary>
    /// Represents the musical grid interval for quantization.
    /// </summary>
    public enum GridInterval
    {
        /// <summary>
        /// Quantize to the nearest whole note.
        /// </summary>
        WholeNote,

        /// <summary>
        /// Quantize to the nearest half note.
        /// </summary>
        HalfNote,

        /// <summary>
        /// Quantize to the nearest quarter note.
        /// </summary>
        QuarterNote,

        /// <summary>
        /// Quantize to the nearest eighth note.
        /// </summary>
        EighthNote,

        /// <summary>
        /// Quantize to the nearest sixteenth note.
        /// </summary>
        SixteenthNote,

        /// <summary>
        /// Quantize to the nearest thirty-second note.
        /// </summary>
        ThirtySecondNote,

        /// <summary>
        /// Quantize to the nearest half-note triplet.
        /// </summary>
        HalfTriplet,

        /// <summary>
        /// Quantize to the nearest quarter-note triplet.
        /// </summary>
        QuarterTriplet,

        /// <summary>
        /// Quantize to the nearest eighth-note triplet.
        /// </summary>
        EighthTriplet,

        /// <summary>
        /// Quantize to the nearest sixteenth-note triplet.
        /// </summary>
        SixteenthTriplet
    }

    /// <summary>
    /// Gets the grid interval to quantize to.
    /// </summary>
    public GridInterval Grid { get; init; } = GridInterval.SixteenthNote;

    /// <summary>
    /// Gets the strength of the quantization (0.0 to 1.0).
    /// 0.0 means no change, 1.0 means notes are moved exactly to the grid.
    /// </summary>
    public double Strength { get; init; } = 1.0;

    /// <summary>
    /// Gets a value indicating whether to quantize the end of the note as well.
    /// </summary>
    public bool QuantizeNoteEnd { get; init; } = false;

    /// <summary>
    /// Gets the swing amount (0.0 to 1.0).
    /// A value of 0.5 is no swing. Greater than 0.5 delays off-beats.
    /// </summary>
    public double Swing { get; init; } = 0.5;
}

/// <summary>
/// A stateless utility class that provides logic for MIDI quantization.
/// </summary>
public static class MidiQuantizer
{
    /// <summary>
    /// Calculates the modifications needed to quantize a collection of MIDI notes.
    /// This is a pure function that does not alter the input notes.
    /// </summary>
    /// <param name="notes">The collection of notes to be quantized.</param>
    /// <param name="settings">The quantization settings to apply.</param>
    /// <param name="ticksPerQuarterNote">The time division of the sequence.</param>
    /// <returns>A list of <see cref="NoteModification"/> objects describing the required changes.</returns>
    public static List<NoteModification> CalculateQuantization(
        IEnumerable<MidiNote> notes,
        QuantizationSettings settings,
        int ticksPerQuarterNote)
    {
        var modifications = new List<NoteModification>();
        var gridTicks = GetGridTicks(settings.Grid, ticksPerQuarterNote);

        foreach (var note in notes)
        {
            var nearestGridTick = (long)Math.Round((double)note.StartTick / gridTicks) * gridTicks;

            // Apply swing
            if (Math.Abs(settings.Swing - 0.5) > 1e-6)
            {
                var gridIndex = (long)Math.Round((double)note.StartTick / gridTicks);
                if (gridIndex % 2 != 0) // Is it an off-beat?
                {
                    var swingOffset = (long)(gridTicks * (settings.Swing - 0.5));
                    nearestGridTick += swingOffset;
                }
            }

            var tickDelta = nearestGridTick - note.StartTick;
            var newStartTick = note.StartTick + (long)(tickDelta * settings.Strength);

            long? newDurationTicks = null;
            if (settings.QuantizeNoteEnd)
            {
                var noteEndTick = note.StartTick + note.DurationTicks;
                var nearestEndGridTick = (long)Math.Round((double)noteEndTick / gridTicks) * gridTicks;
                var endTickDelta = nearestEndGridTick - noteEndTick;
                var newEndTick = noteEndTick + (long)(endTickDelta * settings.Strength);
                newDurationTicks = newEndTick - newStartTick;
                if (newDurationTicks < 1) newDurationTicks = 1; // Ensure duration is at least 1 tick.
            }

            modifications.Add(new NoteModification
            {
                NoteId = note.Id,
                NewStartTick = newStartTick,
                NewDurationTicks = newDurationTicks
            });
        }

        return modifications;
    }

    private static long GetGridTicks(QuantizationSettings.GridInterval grid, int ticksPerQuarterNote)
    {
        return grid switch
        {
            QuantizationSettings.GridInterval.WholeNote => ticksPerQuarterNote * 4,
            QuantizationSettings.GridInterval.HalfNote => ticksPerQuarterNote * 2,
            QuantizationSettings.GridInterval.QuarterNote => ticksPerQuarterNote,
            QuantizationSettings.GridInterval.EighthNote => ticksPerQuarterNote / 2,
            QuantizationSettings.GridInterval.SixteenthNote => ticksPerQuarterNote / 4,
            QuantizationSettings.GridInterval.ThirtySecondNote => ticksPerQuarterNote / 8,
            QuantizationSettings.GridInterval.HalfTriplet => (ticksPerQuarterNote * 4) / 3,
            QuantizationSettings.GridInterval.QuarterTriplet => (ticksPerQuarterNote * 2) / 3,
            QuantizationSettings.GridInterval.EighthTriplet => ticksPerQuarterNote / 3,
            QuantizationSettings.GridInterval.SixteenthTriplet => ticksPerQuarterNote / 6,
            _ => ticksPerQuarterNote / 4
        };
    }
}