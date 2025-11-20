using System.Text;
using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Utilities;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Readers.Format;

internal class M4aReader : BaseSoundFormatReader
{
    public override async Task<Result<SoundFormatInfo>> ReadAsync(Stream stream, ReadOptions options)
    {
        var info = new SoundFormatInfo
        {
            FormatName = "M4A/MP4", 
            FormatIdentifier = "m4a",
            BitrateMode = BitrateMode.VBR
        };
        
        try
        {
            var parseResult = await ParseBoxesAsync(new BigEndianBinaryReader(stream), stream.Length, info, options);
            if (parseResult.IsFailure) return Result<SoundFormatInfo>.Fail(parseResult.Error!);
        
            if (info.SampleRate == 0 || info.Duration.TotalSeconds <= 0)
                return new CorruptChunkError("moov", "The essential metadata atom 'moov' is missing or incomplete.");

            // Calculate bitrate from file size and duration as a reliable fallback.
            if (info.Bitrate == 0 && info.Duration.TotalSeconds > 0)
                info.Bitrate = (int)(stream.Length * 8 / info.Duration.TotalSeconds);
        }
        catch (EndOfStreamException ex)
        {
            return new CorruptChunkError("Atom", "File is truncated or an atom size is incorrect.", ex);
        }

        return info;
    }

    private async Task<Result> ParseBoxesAsync(BigEndianBinaryReader reader, long parentBoxEnd, SoundFormatInfo info, ReadOptions options)
    {
        // A box needs at least 8 bytes for size and type.
        while (reader.BaseStream.Position <= parentBoxEnd - 8)
        {
            var boxSize = reader.ReadUInt32();
            var boxType = reader.ReadFixedString(4);
            
            // Handle 64-bit box size, where the initial size field is 1.
            if (boxSize == 1)
            {
                if (reader.BaseStream.Position > parentBoxEnd - 8) break;
                boxSize = (uint)reader.ReadInt64();
            }
            if (boxSize < 8)
                return new CorruptChunkError(boxType, "Atom size is invalid (less than 8 bytes).");

            var currentBoxPosition = reader.BaseStream.Position - 8;
            var nextBoxPosition = currentBoxPosition + boxSize;
            
            // Ensure we don't read past the parent box's boundary.
            if (nextBoxPosition > parentBoxEnd)
                return new CorruptChunkError(boxType, "Atom size exceeds parent's boundaries.");

            switch (boxType)
            {
                // Parent boxes that we need to recurse into
                case "moov": // Movie Box (contains all metadata)
                case "trak": // Track Box
                case "mdia": // Media Box
                case "minf": // Media Information Box
                case "stbl": // Sample Table Box
                case "udta": // User Data Box
                case "ilst": // iTunes-style metadata list
                    var subResult = await ParseBoxesAsync(reader, nextBoxPosition, info, options);
                    if(subResult.IsFailure) return subResult;
                    break;
                
                // The 'meta' box has a 4-byte null header before its children
                case "meta": 
                    reader.ReadBytes(4);
                    var metaResult = await ParseBoxesAsync(reader, nextBoxPosition, info, options);
                    if(metaResult.IsFailure) return metaResult;
                    break;

                // Boxes with data we need to parse directly
                case "mvhd": // Movie Header (for duration)
                    ParseMvhdBox(reader, info);
                    break;

                case "stsd": // Sample Description (for codec, channels, sample rate)
                    ParseStsdBox(reader, info);
                    break;
                
                case "©nam": // Title
                case "©ART": // Artist
                case "aART": // Album Artist
                case "©alb": // Album
                case "©gen": // Genre
                case "©day": // Year/Date
                case "©wrt": // Composer
                case "©too": // Encoder Tool
                case "©lyr": // Lyrics
                case "trkn": // Track Number
                case "disk": // Disc Number
                case "covr": // Cover Art
                    if (options.ReadTags)
                    {
                        info.Tags ??= new SoundTags();
                        // Subtract 8 for the size/type header of this tag atom.
                        await ParseTagBoxAsync(reader, boxType, info.Tags, options, (long)boxSize - 8);
                    }
                    break;
            }

            // Seek to the start of the next box to ensure we don't fail on unknown boxes.
            if (reader.BaseStream.Position != nextBoxPosition)
            {
                reader.BaseStream.Position = nextBoxPosition;
            }
        }
        return Result.Ok();
    }
    
    private void ParseMvhdBox(BigEndianBinaryReader reader, SoundFormatInfo info)
    {
        var version = reader.ReadByte();
        reader.ReadBytes(3); // flags

        long timescale, duration;
        if (version == 1) // 64-bit version
        {
            reader.ReadBytes(16); // creation and modification times (64-bit)
            timescale = reader.ReadUInt32();
            duration = reader.ReadInt64();
        }
        else // version 0 (32-bit)
        {
            reader.ReadBytes(8); // creation and modification times (32-bit)
            timescale = reader.ReadUInt32();
            duration = reader.ReadUInt32();
        }

        if (duration > 0 && timescale > 0)
            info.Duration = TimeSpan.FromSeconds((double)duration / timescale);
    }
    
    private void ParseStsdBox(BigEndianBinaryReader reader, SoundFormatInfo info)
    {
        reader.ReadBytes(4); // version and flags
        if (reader.ReadUInt32() == 0) return; // entry count

        // We only care about the first audio sample description
        reader.ReadUInt32(); // sample desc size
        var format = reader.ReadFixedString(4);
        reader.ReadBytes(6); // reserved
        reader.ReadUInt16(); // data ref index

        reader.ReadBytes(8); // QT v1 stuff
        info.ChannelCount = reader.ReadUInt16();
        info.BitsPerSample = reader.ReadUInt16();
        reader.ReadBytes(4); // QT stuff

        // The sample rate is a 16.16 fixed-point number.
        info.SampleRate = (int)(reader.ReadUInt32() >> 16);
        
        info.CodecName = format switch
        {
            "mp4a" => "AAC",
            "alac" => "ALAC",
            _ => format.Trim()
        };
        info.IsLossless = (format == "alac");
        info.ContainerVersion = $"MPEG-4 ({info.CodecName})";
    }

    private async Task ParseTagBoxAsync(BigEndianBinaryReader reader, string tagType, SoundTags tags, ReadOptions options, long tagBoxContentSize)
    {
        try
        {
            // Inside a tag atom (e.g., "©nam"), there is a child "data" atom.
            var dataBoxSize = reader.ReadUInt32();
            var dataBoxType = reader.ReadFixedString(4);
            
            // Validate the 'data' atom.
            if (dataBoxType != "data" || dataBoxSize < 16 || dataBoxSize > tagBoxContentSize) return;

            reader.ReadBytes(8); // version, flags (4 bytes) and null locale indicator (4 bytes)
            var dataSize = (int)dataBoxSize - 16;
            if (dataSize <= 0) return;

            var data = new byte[dataSize];
            await reader.BaseStream.ReadExactlyAsync(data);
            
            // The tag VALUE is encoded in UTF-8
            string GetString() => Encoding.UTF8.GetString(data).TrimEnd('\0');

            switch (tagType)
            {
                case "©nam": tags.Title = GetString(); break;
                case "©ART": tags.Artist = GetString(); break;
                case "©alb": tags.Album = GetString(); break;
                case "©gen": tags.Genre = GetString(); break;
                case "©lyr": tags.Lyrics = GetString(); break;
                
                case "©day":
                    var yearString = GetString();
                    if (yearString.Length >= 4 && uint.TryParse(yearString.AsSpan(0, 4), out var year)) 
                        tags.Year = year;
                    break;
                case "trkn":
                    if (data.Length >= 4) tags.TrackNumber = (uint)((data[2] << 8) | data[3]);
                    break;
                case "covr":
                    if (options.ReadAlbumArt) tags.AlbumArt = data;
                    break;
            }
        }
        catch
        {
            // Ignore tag parsing errors and continue
        }
    }
}