using System.Text;
using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Readers.Tags;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Readers.Format;

internal class Mp3Reader : BaseSoundFormatReader
{
    private static readonly int[,] BitrateTable =
    {
        { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 }, // MPEG 2.5
        { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 }, // MPEG 2
        { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 } // MPEG 1
    };

    private static readonly int[,] SampleRateTable =
    {
        { 11025, 12000, 8000 }, // MPEG 2.5
        { 22050, 24000, 16000 }, // MPEG 2
        { 44100, 48000, 32000 } // MPEG 1
    };

    private static readonly string[] StandardGenres =
    [
        "Blues", "Classic Rock", "Country", "Dance", "Disco", "Funk", "Grunge", "Hip-Hop", "Jazz", "Metal",
        "New Age", "Oldies", "Other", "Pop", "R&B", "Rap", "Reggae", "Rock", "Techno", "Industrial",
        "Alternative", "Ska", "Death Metal", "Pranks", "Soundtrack", "Euro-Techno", "Ambient", "Trip-Hop",
        "Vocal", "Jazz+Funk", "Fusion", "Trance", "Classical", "Instrumental", "Acid", "House", "Game",
        "Sound Clip", "Gospel", "Noise", "AlternRock", "Bass", "Soul", "Punk", "Space", "Meditative",
        "Instrumental Pop", "Instrumental Rock", "Ethnic", "Gothic", "Darkwave", "Techno-Industrial",
        "Electronic", "Pop-Folk", "Eurodance", "Dream", "Southern Rock", "Comedy", "Cult", "Gangsta",
        "Top 40", "Christian Rap", "Pop/Funk", "Jungle", "Native American", "Cabaret", "New Wave",
        "Psychadelic", "Rave", "Showtunes", "Trailer", "Lo-Fi", "Tribal", "Acid Punk", "Acid Jazz", "Polka",
        "Freestyle", "Duet", "Punk Rock", "Drum Solo", "Acapella", "Euro-House", "Dance Hall", "Goa",
        "Drum & Bass", "Club-House", "Hardcore", "Terror", "Indie", "BritPop", "Negerpunk", "Polsk Punk",
        "Beat", "Christian Gangsta Rap", "Heavy Metal", "Black Metal", "Crossover", "Contemporary Christian",
        "Christian Rock", "Merengue", "Salsa", "Thrash Metal", "Anime", "Jpop", "Synthpop", "Abstract",
        "Art Rock", "Baroque", "Bhangra", "Big Beat", "Breakbeat", "Chillout", "Downtempo", "Dub", "EBM",
        "Eclectic", "Electro", "Electroclash", "Emo", "Experimental", "Garage", "Global", "IDM", "Illbient",
        "Industro-Goth", "Jam Band", "Krautrock", "Leftfield", "Lounge", "Math Rock", "New Romantic",
        "Nu-Breakz", "Post-Punk", "Post-Rock", "Psytrance", "Shoegaze", "Space Rock", "Trop Rock",
        "World Music", "Neoclassical", "Audiobook", "Audio-Theatre", "Neue Deutsche Welle", "Podcast",
        "Indie Rock", "G-Funk", "Dubstep", "Garage Rock", "Psybient"
    ];

    public override async Task<Result<SoundFormatInfo>> ReadAsync(Stream stream, ReadOptions options)
    {
        var info = new SoundFormatInfo 
        { 
            FormatName = "MP3",
            CodecName = "MPEG Layer III",
            FormatIdentifier = "mp3", 
            IsLossless = false
        };
        long audioDataStart = 0;
        var streamLength = stream.Length;

        // Read ID3v2 tag if requested
        if (options.ReadTags)
        {
            var id3Reader = new Id3V2Reader();
            var id3Result = await id3Reader.ReadAsync(stream, options);
            if (id3Result.IsFailure) return Result<SoundFormatInfo>.Fail(id3Result.Error!);
            var (tag, tagSize) = id3Result.Value;
            
            info.Tags = tag;
            audioDataStart = tagSize;
        }

        // If no ID3v2 tags were found, try to read ID3v1 tags from the end of the file.
        if (info.Tags == null && options.ReadTags) info.Tags = await TryReadId3V1TagAsync(stream);

        stream.Position = audioDataStart;

        // Find first frame header
        var headerBuffer = new byte[4];
        while (stream.Position < streamLength - 4)
        {
            if (await stream.ReadAsync(headerBuffer.AsMemory(0, 1)) == 0) break;
            if (headerBuffer[0] != 0xFF) continue;

            if (await stream.ReadAsync(headerBuffer.AsMemory(1, 1)) == 0) break;
            if ((headerBuffer[1] & 0xE0) != 0xE0) continue;

            // Found a sync word, read rest of header and process
            if (await stream.ReadAsync(headerBuffer.AsMemory(2, 2)) != 2) continue;
            var parseResult = ParseFrameHeader(headerBuffer, info);
            if(parseResult.IsFailure) return Result<SoundFormatInfo>.Fail(parseResult.Error!);

            if (options.DurationAccuracy == DurationAccuracy.AccurateScan)
            {
                try
                {
                    await TryReadVbrHeaderAsync(stream, headerBuffer, info);
                }
                catch (EndOfStreamException ex)
                {
                    return new CorruptFrameError("MP3 Xing/VBRI", "File is truncated or VBR header is malformed.", ex);
                }
            }

            // Estimate duration if not accurately determined
            if (info.Duration == TimeSpan.Zero && info.Bitrate > 0)
            {
                var audioDataLength = streamLength - audioDataStart;
                info.Duration = TimeSpan.FromSeconds((double)audioDataLength * 8 / info.Bitrate);
                info.BitrateMode = BitrateMode.CBR; // Assumed constant bitrate if vbr header not found
            }

            return info;
        }

        return new HeaderNotFoundError("MP3 Frame");
    }

    private Result ParseFrameHeader(byte[] header, SoundFormatInfo info)
    {
        var mpegVersionId = (header[1] >> 3) & 0x03;
        var layerId = (header[1] >> 1) & 0x03;
        var bitrateIndex = (header[2] >> 4) & 0x0F;
        var sampleRateIndex = (header[2] >> 2) & 0x03;
        var channelMode = (header[3] >> 6) & 0x03;

        // Check for invalid values
        if (bitrateIndex == 0 || bitrateIndex == 15 || sampleRateIndex == 3)
            return new CorruptFrameError("MP3", $"Invalid frame parameters: BitrateIndex: {bitrateIndex}, SampleRateIndex: {sampleRateIndex}");

        // Determine MPEG version
        int mpegVersionIndex;
        switch (mpegVersionId)
        {
            case 0:
                mpegVersionIndex = 0;
                info.ContainerVersion = "MPEG 2.5";
                break;
            case 2:
                mpegVersionIndex = 1;
                info.ContainerVersion = "MPEG 2";
                break;
            case 3:
                mpegVersionIndex = 2;
                info.ContainerVersion = "MPEG 1";
                break;
            default: 
                return new CorruptFrameError("MP3", "Invalid MPEG version.");
        }

        // Determine layer and update codec name accordingly
        string layerName;
        switch (layerId)
        {
            case 1:
                layerName = "Layer III";
                info.CodecName = "MPEG Layer III";
                break;
            case 2:
                layerName = "Layer II";
                info.CodecName = "MPEG Layer II";
                break;
            case 3:
                layerName = "Layer I";
                info.CodecName = "MPEG Layer I";
                break;
            default:
                return new CorruptFrameError("MP3", "Invalid MPEG layer.");
        }

        // Use appropriate bitrate table based on version and layer
        int[,] layerBitrateTable;
        if (mpegVersionIndex == 2) // MPEG 1
        {
            layerBitrateTable = layerId switch
            {
                1 => BitrateTable,
                2 => new[,] { { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 } },
                _ => new[,] { { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 } }
            };
        }
        else // MPEG 2 or 2.5
        {
            layerBitrateTable = layerId == 1 ? BitrateTable : 
                               new[,] { { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 } };
        }

        info.Bitrate = layerBitrateTable[0, bitrateIndex] * 1000;
        info.SampleRate = SampleRateTable[mpegVersionIndex, sampleRateIndex];
        info.ChannelCount = channelMode == 3 ? 1 : 2;
        
        // Add layer information to format details
        info.FormatName = $"MP3 ({layerName})";
        return Result.Ok();
    }

    private async Task TryReadVbrHeaderAsync(Stream stream, byte[] firstFrameHeader, SoundFormatInfo info)
    {
        var originalPos = stream.Position;

        // 1. Check for a Xing Header
        var mpegVersionId = (firstFrameHeader[1] >> 3) & 0x03;
        var channelMode = (firstFrameHeader[3] >> 6) & 0x03;
        var isMpeg1 = mpegVersionId == 3;
        var isMono = channelMode == 3;

        var xingOffset = isMpeg1 ? (isMono ? 17 : 32) : (isMono ? 9 : 17);
        stream.Position = originalPos + xingOffset; // Position is after the 4-byte header

        var headerSignature = new byte[4];
        await stream.ReadExactlyAsync(headerSignature, 0, 4);
        var xingId = Encoding.ASCII.GetString(headerSignature);

        if (xingId is "Xing" or "Info")
        {
            info.BitrateMode = BitrateMode.VBR;
            var flagsBuffer = new byte[4];
            await stream.ReadExactlyAsync(flagsBuffer, 0, 4);
            Array.Reverse(flagsBuffer);
            var flags = BitConverter.ToUInt32(flagsBuffer, 0);

            uint totalFrames = 0;
            uint totalBytes = 0;

            // Check if Frames flag is set
            if ((flags & 0x01) != 0)
            {
                var framesBuffer = new byte[4];
                await stream.ReadExactlyAsync(framesBuffer, 0, 4);
                Array.Reverse(framesBuffer);
                totalFrames = BitConverter.ToUInt32(framesBuffer, 0);
            }

            // Check if Bytes flag is set
            if ((flags & 0x02) != 0)
            {
                var bytesBuffer = new byte[4];
                await stream.ReadExactlyAsync(bytesBuffer, 0, 4);
                Array.Reverse(bytesBuffer);
                totalBytes = BitConverter.ToUInt32(bytesBuffer, 0);
            }

            if (totalFrames > 0)
            {
                var samplesPerFrame = isMpeg1 ? 1152 : 576;
                info.Duration = TimeSpan.FromSeconds((double)totalFrames * samplesPerFrame / info.SampleRate);
                if (totalBytes > 0 && info.Duration.TotalSeconds > 0)
                    info.Bitrate = (int)(totalBytes * 8 / info.Duration.TotalSeconds);
            }
        }
        else
        {
            // Check for a VBRI Header
            stream.Position = originalPos + 32; // VBRI header is at a fixed 32-byte offset after the frame header
            await stream.ReadExactlyAsync(headerSignature, 0, 4);
            var vbriId = Encoding.ASCII.GetString(headerSignature);

            if (vbriId == "VBRI")
            {
                info.BitrateMode = BitrateMode.VBR;
                stream.Position += 6; // Skip version and delay

                var bytesBuffer = new byte[4];
                await stream.ReadExactlyAsync(bytesBuffer, 0, 4);
                Array.Reverse(bytesBuffer);
                var totalBytes = BitConverter.ToUInt32(bytesBuffer, 0);

                var framesBuffer = new byte[4];
                await stream.ReadExactlyAsync(framesBuffer, 0, 4);
                Array.Reverse(framesBuffer);
                var totalFrames = BitConverter.ToUInt32(framesBuffer, 0);

                if (totalFrames > 0)
                {
                    var samplesPerFrame = isMpeg1 ? 1152 : 576;
                    info.Duration = TimeSpan.FromSeconds((double)totalFrames * samplesPerFrame / info.SampleRate);
                    if (totalBytes > 0 && info.Duration.TotalSeconds > 0)
                        info.Bitrate = (int)(totalBytes * 8 / info.Duration.TotalSeconds);
                }
            }
        }

        stream.Position = originalPos;
    }

    private async Task<SoundTags?> TryReadId3V1TagAsync(Stream stream)
    {
        if (stream.Length < 128) return null;

        var originalPosition = stream.Position;
        try
        {
            stream.Position = stream.Length - 128;
            var buffer = new byte[128];
            if (await stream.ReadAsync(buffer) < 128) return null;

            if (Encoding.ASCII.GetString(buffer, 0, 3) != "TAG") return null;

            var tags = new SoundTags();
            var iso88591 = Encoding.GetEncoding("ISO-8859-1");

            string GetString(byte[] src, int offset, int count)
            {
                var terminator = Array.IndexOf(src, (byte)0, offset, count);
                var length = terminator == -1 ? count : terminator - offset;
                return iso88591.GetString(src, offset, length).Trim();
            }

            tags.Title = GetString(buffer, 3, 30);
            tags.Artist = GetString(buffer, 33, 30);
            tags.Album = GetString(buffer, 63, 30);
            var yearString = GetString(buffer, 93, 4);
            if (uint.TryParse(yearString, out var year)) tags.Year = year;

            // ID3v1.1: If byte 125 is null and byte 126 is non-null, it's a track number.
            if (buffer[125] == 0 && buffer[126] != 0) tags.TrackNumber = buffer[126];

            var genreId = buffer[127];
            if (genreId < StandardGenres.Length) tags.Genre = StandardGenres[genreId];

            return tags;
        }
        catch
        {
            return null;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}