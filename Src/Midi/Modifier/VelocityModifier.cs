using SoundFlow.Midi.Abstracts;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Structs;

namespace SoundFlow.Midi.Modifier;

/// <summary>
/// A MIDI modifier that reshapes the velocity of Note On messages.
/// </summary>
public sealed class VelocityModifier : MidiModifier
{
    /// <inheritdoc />
    public override string Name => "Velocity";

    /// <summary>
    /// Gets or sets the velocity curve.
    /// -1.0 (logarithmic) makes it easier to play loud notes.
    /// 0.0 is linear (no change).
    /// +1.0 (exponential) makes it harder to play loud notes.
    /// </summary>
    public float Curve { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the minimum output velocity (1-127).
    /// </summary>
    public int MinVelocity { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum output velocity (1-127).
    /// </summary>
    public int MaxVelocity { get; set; } = 127;

    /// <summary>
    /// Gets or sets a fixed value to add to the velocity after curve and range mapping.
    /// </summary>
    public int Add { get; set; } = 0;
    
    /// <inheritdoc />
    public override IEnumerable<MidiMessage> Process(MidiMessage message)
    {
        if (!IsEnabled)
        {
            yield return message;
            yield break;
        }
        
        // Only process Note On messages with a velocity greater than 0.
        if (message is { Command: MidiCommand.NoteOn, Velocity: > 0 })
        {
            var normalizedVelocity = message.Velocity / 127.0f;

            // Apply the velocity curve if it's not linear.
            if (Curve != 0.0f)
            {
                // We use 2^x as an intuitive mapping for the curve exponent.
                var exponent = Math.Pow(2, -Math.Clamp(Curve, -1.0f, 1.0f));
                normalizedVelocity = (float)Math.Pow(normalizedVelocity, exponent);
            }

            // Map to the specified Min/Max range.
            var min = Math.Clamp(MinVelocity, 1, 127);
            var max = Math.Clamp(MaxVelocity, 1, 127);
            var newVelocity = normalizedVelocity * (max - min) + min;

            // Apply the final additive offset.
            newVelocity += Add;

            var finalVelocity = (byte)Math.Clamp(newVelocity, 1, 127);
            yield return new MidiMessage(message.StatusByte, message.Data1, finalVelocity, message.Timestamp);
        }
        else
        {
            // Pass through all other message types unchanged.
            yield return message;
        }
    }
}