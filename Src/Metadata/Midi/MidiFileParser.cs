using System.Text;
using SoundFlow.Metadata.Midi.Enums;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Metadata.Midi;

/// <summary>
/// A parser for Standard MIDI Files (.mid), capable of reading header and track chunks,
/// handling various event types, and supporting running status.
/// </summary>
public static class MidiFileParser
{
    /// <summary>
    /// Parses a MIDI file from the given stream.
    /// </summary>
    /// <param name="stream">The stream containing the MIDI file data. Must be readable.</param>
    /// <returns>A <see cref="MidiFile"/> object representing the parsed data.</returns>
    /// <exception cref="InvalidDataException">Thrown if the file is not a valid MIDI file.</exception>
    public static MidiFile Parse(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, true);

        // Read MThd header chunk
        if (ReadChunkId(reader) != "MThd")
            throw new InvalidDataException("Not a valid MIDI file. Missing 'MThd' header.");

        var headerLength = ReadBigEndianInt32(reader);
        if (headerLength != 6)
            throw new InvalidDataException("MIDI header has an unexpected length.");
        
        var midiFile = new MidiFile
        {
            Format = ReadBigEndianInt16(reader)
        };
        
        // The MIDI header format specifies track count before time division.
        var trackCount = ReadBigEndianInt16(reader);
        var timeDivision = ReadBigEndianInt16(reader);
        
        if ((timeDivision & 0x8000) != 0)
        {
            // SMPTE time code format - not supported
            throw new NotSupportedException("SMPTE time code format is not supported.");
        }

        // Ticks per quarter note format
        midiFile.TicksPerQuarterNote = timeDivision;

        // Loop through all chunks until the end of the stream instead of a rigid for loop.
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var chunkId = ReadChunkId(reader);
            var chunkLength = ReadBigEndianInt32(reader);

            if (chunkId == "MTrk")
            {
                var track = ParseTrackChunk(reader, chunkLength);
                midiFile.AddTrack(track);
            }
            else
            {
                // This is an unknown or non-track chunk. Read its length and skip it.
                Log.Error($"Skipping unknown chunk type '{chunkId}' of length {chunkLength}.");
                reader.BaseStream.Seek(chunkLength, SeekOrigin.Current);
            }
        }

        return midiFile;
    }

    private static MidiTrack ParseTrackChunk(BinaryReader reader, long trackLength)
    {
        var trackEndPosition = reader.BaseStream.Position + trackLength;
        
        var track = new MidiTrack();
        byte lastStatusByte = 0;

        while (reader.BaseStream.Position < trackEndPosition)
        {
            var deltaTime = ReadVariableLengthQuantity(reader);
            var statusByte = reader.ReadByte();

            // Check if this is a running status situation
            if ((statusByte & 0x80) == 0)
            {
                // This is a data byte, use running status
                if (lastStatusByte == 0)
                    throw new InvalidDataException("Invalid MIDI event. Running status used before a status byte was set.");
                
                // The byte we read was the first data byte, so rewind and use the last status.
                reader.BaseStream.Position--;
                statusByte = lastStatusByte;
            }
            else
            {
                // This is a new status byte
                lastStatusByte = statusByte;
            }

            MidiEvent midiEvent;

            switch (statusByte & 0xF0)
            {
                case 0x80: // Note Off
                case 0x90: // Note On
                case 0xA0: // Polyphonic Key Pressure
                case 0xB0: // Control Change
                case 0xE0: // Pitch Bend
                    var data1 = reader.ReadByte();
                    var data2 = reader.ReadByte();
                    midiEvent = new ChannelEvent(deltaTime, new MidiMessage(statusByte, data1, data2));
                    break;
                case 0xC0: // Program Change
                case 0xD0: // Channel Pressure
                    var program = reader.ReadByte();
                    midiEvent = new ChannelEvent(deltaTime, new MidiMessage(statusByte, program, 0));
                    break;
                default:
                    switch (statusByte)
                    {
                        case 0xFF: // Meta Event
                            var metaType = reader.ReadByte();
                            var metaLength = ReadVariableLengthQuantity(reader);
                            var metaData = reader.ReadBytes((int)metaLength);
                            midiEvent = new MetaEvent(deltaTime, (MetaEventType)metaType, metaData);
                            if (metaType == (byte)MetaEventType.EndOfTrack)
                            {
                                track.AddEvent(midiEvent);
                                // Ensure we are at the end of the chunk after an EndOfTrack event
                                if(reader.BaseStream.Position < trackEndPosition)
                                {
                                    reader.BaseStream.Position = trackEndPosition;
                                }
                                return track;
                            }
                            break;
                        case 0xF0: // System Exclusive
                        case 0xF7: // SysEx continuation or escape
                            var sysexLength = ReadVariableLengthQuantity(reader);
                            var sysexData = reader.ReadBytes((int)sysexLength);
                            midiEvent = new SysExEvent(deltaTime, sysexData);
                            break;
                        default:
                            throw new InvalidDataException($"Unknown MIDI event status: 0x{statusByte:X2}");
                    }
                    break;
            }
            track.AddEvent(midiEvent);
        }

        return track;
    }

    private static string ReadChunkId(BinaryReader reader) => new(reader.ReadChars(4));
    
    private static int ReadBigEndianInt32(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (bytes.Length < 4) throw new EndOfStreamException("Unexpected end of stream while reading 32-bit integer.");
        return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
    }
    
    private static short ReadBigEndianInt16(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(2);
        if (bytes.Length < 2) throw new EndOfStreamException("Unexpected end of stream while reading 16-bit integer.");
        return (short)((bytes[0] << 8) | bytes[1]);
    }

    private static long ReadVariableLengthQuantity(BinaryReader reader)
    {
        long value = 0;
        byte b;
        var byteCount = 0;
        do
        {
            if (byteCount++ > 4) throw new InvalidDataException("Variable-length quantity is too long.");
            b = reader.ReadByte();
            value = (value << 7) + (b & 0x7F);
        } while ((b & 0x80) != 0);
        return value;
    }
}