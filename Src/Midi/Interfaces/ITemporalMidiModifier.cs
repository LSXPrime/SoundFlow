using SoundFlow.Midi.Structs;

namespace SoundFlow.Midi.Interfaces;

/// <summary>
/// Defines an interface for MIDI modifiers that require time-based updates to generate events.
/// </summary>
public interface ITemporalMidiModifier
{
    /// <summary>
    /// Advances the modifier's internal state by a specified duration and returns any generated MIDI messages.
    /// </summary>
    /// <param name="deltaSeconds">The time elapsed since the last tick, in seconds.</param>
    /// <param name="bpm">The current tempo in beats per minute.</param>
    /// <returns>A collection of generated MIDI messages, or an empty collection if no events occurred.</returns>
    IEnumerable<MidiMessage> Tick(double deltaSeconds, double bpm);
}