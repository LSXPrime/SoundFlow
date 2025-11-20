using SoundFlow.Editing;

namespace SoundFlow.Synthesis.Interfaces;

/// <summary>
/// Defines the context required by a Sequencer to perform tempo-aware time conversions.
/// This allows the Sequencer to remain decoupled from the main Composition object.
/// </summary>
public interface ISequencerContext
{
    /// <summary>
    /// Gets the master tempo track, defining tempo changes over time.
    /// </summary>
    IReadOnlyList<TempoMarker> TempoTrack { get; }
    
    /// <summary>
    /// Gets the time division for MIDI data in ticks per quarter note.
    /// </summary>
    int TicksPerQuarterNote { get; }
}