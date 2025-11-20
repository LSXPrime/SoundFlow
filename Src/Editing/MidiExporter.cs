using SoundFlow.Metadata.Midi;
using SoundFlow.Metadata.Midi.Enums;
using SoundFlow.Utils;

namespace SoundFlow.Editing;

/// <summary>
/// Provides functionality to export a <see cref="Composition"/> to a Standard MIDI File.
/// </summary>
public static class MidiExporter
{
    /// <summary>
    /// Asynchronously exports the entire composition to a Format 1 Standard MIDI File.
    /// </summary>
    /// <param name="composition">The composition to export.</param>
    /// <param name="filePath">The path where the MIDI file will be saved.</param>
    public static async Task ExportAsync(Composition composition, string filePath)
    {
        var ticksPerQuarterNote = composition.TicksPerQuarterNote;

        var midiFile = new MidiFile { Format = 1, TicksPerQuarterNote = ticksPerQuarterNote };

        // 1. Create Conductor Track (Track 0) for tempo and time signature
        var conductorTrack = new Metadata.Midi.MidiTrack();
        // Add track name meta event
        conductorTrack.AddEvent(new MetaEvent(0, MetaEventType.TrackName, "Conductor"u8.ToArray()));

        foreach (var marker in composition.TempoTrack)
        {
            var tick = MidiTimeConverter.GetTickForTimeSpan(marker.Time, ticksPerQuarterNote, composition.TempoTrack);
            var microsecondsPerQuarterNote = (int)(60_000_000.0 / marker.BeatsPerMinute);
            var tempoData = new[]
            {
                (byte)((microsecondsPerQuarterNote >> 16) & 0xFF),
                (byte)((microsecondsPerQuarterNote >> 8) & 0xFF),
                (byte)(microsecondsPerQuarterNote & 0xFF)
            };
            conductorTrack.AddEvent(new MetaEvent(tick, MetaEventType.SetTempo, tempoData));
        }

        midiFile.AddTrack(FinalizeTrack(conductorTrack));

        // 2. Create a track for each MIDI track in the composition
        foreach (var compoMidiTrack in composition.MidiTracks)
        {
            var trackEvents = new List<MidiEvent>
            {
                // Add track name
                new MetaEvent(0, MetaEventType.TrackName,
                    System.Text.Encoding.ASCII.GetBytes(compoMidiTrack.Name))
            };

            // Merge and offset all segments
            foreach (var segment in compoMidiTrack.Segments)
            {
                var timelineOffsetTicks = MidiTimeConverter.GetTickForTimeSpan(segment.TimelineStartTime,
                    ticksPerQuarterNote, composition.TempoTrack);
                foreach (var timedEvent in segment.DataProvider.Events)
                {
                    // The event's DeltaTimeTicks is already absolute from its sequence start.
                    var globalTick = timelineOffsetTicks + timedEvent.AbsoluteTimeTicks;
                    trackEvents.Add(timedEvent.Event with { DeltaTimeTicks = globalTick });
                }
            }

            var newMidiFileTrack = new Metadata.Midi.MidiTrack();
            foreach (var evt in trackEvents)
            {
                newMidiFileTrack.AddEvent(evt);
            }

            midiFile.AddTrack(FinalizeTrack(newMidiFileTrack));
        }

        // 3. Write the file
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        MidiFileWriter.Write(midiFile, fileStream);
    }

    /// <summary>
    /// Converts a track with absolute-timed events into one with delta-timed events
    /// and ensures it is properly terminated.
    /// </summary>
    private static Metadata.Midi.MidiTrack FinalizeTrack(Metadata.Midi.MidiTrack absoluteTrack)
    {
        var sortedEvents = absoluteTrack.Events.OrderBy(e => e.DeltaTimeTicks).ToList();
        var finalizedTrack = new Metadata.Midi.MidiTrack();
        long lastTick = 0;

        foreach (var absoluteEvent in sortedEvents)
        {
            var delta = absoluteEvent.DeltaTimeTicks - lastTick;
            finalizedTrack.AddEvent(absoluteEvent with { DeltaTimeTicks = delta });
            lastTick = absoluteEvent.DeltaTimeTicks;
        }

        if (finalizedTrack.Events.LastOrDefault() is not MetaEvent { Type: MetaEventType.EndOfTrack })
        {
            finalizedTrack.AddEvent(new MetaEvent(0, MetaEventType.EndOfTrack, []));
        }

        return finalizedTrack;
    }
}