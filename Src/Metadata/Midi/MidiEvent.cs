using SoundFlow.Metadata.Midi.Enums;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Midi;

/// <summary>
/// Base record for any event found within a MIDI file track.
/// </summary>
/// <param name="DeltaTimeTicks">The time elapsed since the previous event on the same track, in MIDI ticks.</param>
public abstract record MidiEvent(long DeltaTimeTicks);

/// <summary>
/// Represents a MIDI channel message event (e.g., Note On, Control Change).
/// </summary>
/// <param name="DeltaTimeTicks">The time elapsed since the previous event.</param>
/// <param name="Message">The underlying MIDI channel message.</param>
public sealed record ChannelEvent(long DeltaTimeTicks, MidiMessage Message) : MidiEvent(DeltaTimeTicks);

/// <summary>
/// Represents a System Exclusive (SysEx) message event.
/// </summary>
/// <param name="DeltaTimeTicks">The time elapsed since the previous event.</param>
/// <param name="Data">The raw SysEx data bytes.</param>
public sealed record SysExEvent(long DeltaTimeTicks, byte[] Data) : MidiEvent(DeltaTimeTicks);

/// <summary>
/// Represents a meta-event within a MIDI file, containing non-MIDI data like tempo, lyrics, or track name.
/// </summary>
/// <param name="DeltaTimeTicks">The time elapsed since the previous event.</param>
/// <param name="Type">The type of the meta-event.</param>
/// <param name="Data">The raw data associated with the meta-event.</param>
public sealed record MetaEvent(long DeltaTimeTicks, MetaEventType Type, byte[] Data) : MidiEvent(DeltaTimeTicks);