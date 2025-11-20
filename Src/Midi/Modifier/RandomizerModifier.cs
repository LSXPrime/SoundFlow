using SoundFlow.Midi.Abstracts;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Structs;

namespace SoundFlow.Midi.Modifier;

/// <summary>
/// A powerful MIDI modifier that introduces configurable randomness to note messages.
/// It can modify chance, velocity, and pitch to humanize performances or create generative patterns.
/// </summary>
public sealed class RandomizerModifier : MidiModifier
{
    /// <inheritdoc />
    public override string Name => "Randomizer";

    #region Properties

    /// <summary>
    /// Gets or sets the probability (0.0 to 1.0) that a note will be played.
    /// </summary>
    public float Chance { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the amount of randomness (0.0 to 1.0) to apply to note velocity.
    /// </summary>
    public float VelocityRandomness { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets a bias for velocity randomization (-1.0 to 1.0).
    /// Negative values tend towards lower velocities; positive values tend towards higher velocities.
    /// </summary>
    public float VelocityBias { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the probability (0.0 to 1.0) that a note's pitch will be randomized.
    /// </summary>
    public float PitchRandomness { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the maximum range in semitones (+/-) for pitch randomization.
    /// </summary>
    public int PitchRange { get; set; } = 12;

    /// <summary>
    /// Gets or sets the minimum note number (0-127) to apply the modifier to.
    /// </summary>
    public int MinNote { get; set; } = 0;

    /// <summary>
    /// Gets or sets the maximum note number (0-127) to apply the modifier to.
    /// </summary>
    public int MaxNote { get; set; } = 127;

    #endregion

    private static readonly Random Random = new();
    
    private readonly Dictionary<int, int> _activeNoteMap = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RandomizerModifier"/> class with default values.
    /// </summary>
    public RandomizerModifier() { }

    /// <inheritdoc />
    public override IEnumerable<MidiMessage> Process(MidiMessage message)
    {
        // For Pass through if disabled or if the message is not a note.
        if (!IsEnabled || message.Command is not (MidiCommand.NoteOn or MidiCommand.NoteOff))
        {
            yield return message;
            yield break;
        }

        // Pass through if the note is outside the specified range.
        if (message.NoteNumber < MinNote || message.NoteNumber > MaxNote)
        {
            yield return message;
            yield break;
        }

        if (message is { Command: MidiCommand.NoteOn, Velocity: > 0 })
        {
            // Chance to Play
            if (Chance < 1.0f && Random.NextSingle() >= Chance)
            {
                yield break; // Drop the note.
            }

            var originalNote = message.NoteNumber;
            var newNote = originalNote;
            var newVelocity = message.Velocity;

            // Pitch Randomization
            if (PitchRandomness > 0.0f && Random.NextSingle() < PitchRandomness)
            {
                var pitchOffset = Random.Next(-PitchRange, PitchRange + 1);
                newNote = Math.Clamp(originalNote + pitchOffset, 0, 127);
                
                // Store the mapping for the corresponding Note Off message.
                _activeNoteMap[originalNote] = newNote;
            }

            // Velocity Randomization & Bias
            if (VelocityRandomness > 0.0f)
            {
                var randomnessAmount = Math.Clamp(VelocityRandomness, 0.0f, 1.0f);
                var biasAmount = Math.Clamp(VelocityBias, -1.0f, 1.0f);

                var maxDeviation = (int)(randomnessAmount * 127 / 2);
                var offset = Random.Next(-maxDeviation, maxDeviation + 1);
                
                // Apply bias by shifting the random offset towards the bias direction.
                offset += (int)(biasAmount * maxDeviation);

                newVelocity = Math.Clamp(newVelocity + offset, 1, 127);
            }

            yield return new MidiMessage(message.StatusByte, (byte)newNote, (byte)newVelocity, message.Timestamp);
        }
        else // Handle Note Off (or Note On with Velocity 0)
        {
            var originalNote = message.NoteNumber;

            // Remove if this note's pitch was randomized.
            if (_activeNoteMap.Remove(originalNote, out var randomizedNote))
            {
                // Send the Note Off for the randomized pitch and clean up the state.
                yield return new MidiMessage(message.StatusByte, (byte)randomizedNote, message.Data2, message.Timestamp);
            }
            else
            {
                // Otherwise, it was not a randomized note, so pass it through.
                yield return message;
            }
        }
    }
}