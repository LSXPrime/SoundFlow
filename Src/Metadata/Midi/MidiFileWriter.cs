using SoundFlow.Enums;
using SoundFlow.Metadata.Utilities;
using SoundFlow.Midi.Enums;

namespace SoundFlow.Metadata.Midi;

/// <summary>
/// A utility to write a <see cref="MidiFile"/> object to a stream in the Standard MIDI File format.
/// </summary>
public static class MidiFileWriter
{
    /// <summary>
    /// Writes a MidiFile object to the specified stream.
    /// </summary>
    /// <param name="midiFile">The MidiFile object to write.</param>
    /// <param name="stream">The output stream.</param>
    public static void Write(MidiFile midiFile, Stream stream)
    {
        using var writer = new BigEndianBinaryWriter(stream);
        
        // Write MThd header chunk
        writer.Write("MThd"u8.ToArray());
        writer.Write(6); // Header length is always 6
        writer.Write((short)midiFile.Format);
        writer.Write((short)midiFile.Tracks.Count);
        writer.Write((short)midiFile.TicksPerQuarterNote);
        
        // Write each track chunk
        foreach (var track in midiFile.Tracks)
        {
            writer.Write("MTrk"u8.ToArray());

            using var trackStream = new MemoryStream();
            using var trackWriter = new BigEndianBinaryWriter(trackStream);
            
            foreach (var midiEvent in track.Events)
            {
                trackWriter.WriteVariableLengthQuantity(midiEvent.DeltaTimeTicks);
                
                switch (midiEvent)
                {
                    case ChannelEvent ce:
                        trackWriter.Write(ce.Message.StatusByte);
                        trackWriter.Write(ce.Message.Data1);
                        // Some channel events don't have a second data byte
                        if (ce.Message.Command != MidiCommand.ProgramChange && ce.Message.Command != MidiCommand.ChannelPressure)
                        {
                            trackWriter.Write(ce.Message.Data2);
                        }
                        break;
                    
                    case MetaEvent me:
                        trackWriter.Write((byte)0xFF);
                        trackWriter.Write((byte)me.Type);
                        trackWriter.WriteVariableLengthQuantity(me.Data.Length);
                        trackWriter.Write(me.Data);
                        break;
                        
                    case SysExEvent se:
                        trackWriter.Write((byte)0xF0); // SysEx start
                        trackWriter.WriteVariableLengthQuantity(se.Data.Length);
                        trackWriter.Write(se.Data);
                        break;
                }
            }

            // Write track length and data
            writer.Write((int)trackStream.Length);
            trackStream.Position = 0;
            trackStream.CopyTo(stream);
        }
    }
}