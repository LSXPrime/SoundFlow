using SoundFlow.Midi.Abstracts;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Structs;

namespace SoundFlow.Midi.Modifier;

/// <summary>
/// A MIDI modifier that generates a chord from a single incoming note.
/// </summary>
public sealed class HarmonizerModifier : MidiModifier
{
    /// <inheritdoc />
    public override string Name => $"Harmonizer ({Intervals.Length} Notes)";

    /// <summary>
    /// Gets or sets the intervals in semitones relative to the root note.
    /// Example for Major Chord: { 0, 4, 7 }
    /// Example for Power Chord: { 0, 7, 12 }
    /// </summary>
    public int[] Intervals { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HarmonizerModifier"/> class.
    /// </summary>
    /// <param name="intervals">The chord intervals in semitones.</param>
    public HarmonizerModifier(int[] intervals)
    {
        Intervals = intervals;
    }

    /// <inheritdoc />
    public override IEnumerable<MidiMessage> Process(MidiMessage message)
    {
        if (!IsEnabled)
        {
            yield return message;
            yield break;
        }
        
        if (message.Command is MidiCommand.NoteOn or MidiCommand.NoteOff)
        {
            // If there are no intervals, just pass the original message through.
            if (Intervals.Length == 0)
            {
                yield return message;
                yield break;
            }

            // Generate a note for each interval in the chord.
            foreach (var interval in Intervals)
            {
                var newNote = Math.Clamp(message.NoteNumber + interval, 0, 127);
                yield return new MidiMessage(message.StatusByte, (byte)newNote, message.Data2, message.Timestamp);
            }
        }
        else
        {
            // Pass through all other message types unchanged.
            yield return message;
        }
    }
}