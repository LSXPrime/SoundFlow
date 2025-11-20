using SoundFlow.Midi.Abstracts;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Structs;

namespace SoundFlow.Midi.Modifier;

/// <summary>
/// A MIDI modifier that transposes the pitch of Note On and Note Off messages.
/// </summary>
public sealed class TransposeModifier : MidiModifier
{
    /// <inheritdoc />
    public override string Name => $"Transpose ({Semitones} st)";
    
    /// <summary>
    /// Gets or sets the amount to transpose in semitones. Can be positive or negative.
    /// </summary>
    public int Semitones { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransposeModifier"/> class.
    /// </summary>
    /// <param name="semitones">The transposition amount in semitones.</param>
    public TransposeModifier(int semitones)
    {
        Semitones = semitones;
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
            var newNote = Math.Clamp(message.NoteNumber + Semitones, 0, 127);
            yield return new MidiMessage(message.StatusByte, (byte)newNote, message.Data2, message.Timestamp);
        }
        else
        {
            // Pass through all other message types unchanged.
            yield return message;
        }
    }
}