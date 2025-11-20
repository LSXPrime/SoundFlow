using System.Text;
using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Readers.Tags;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Readers.Format;

internal class OggReader : BaseSoundFormatReader
{
    public override async Task<Result<SoundFormatInfo>> ReadAsync(Stream stream, ReadOptions options)
    {
        var info = new SoundFormatInfo
        {
            FormatName = "Ogg", 
            FormatIdentifier = "ogg", 
            IsLossless = false, 
            BitrateMode = BitrateMode.VBR
        };

        try
        {
            // The first page must contain an identification header for the codec.
            var page = await ReadNextPageAsync(stream);
            if (page == null || page.Packets.Count == 0)
                return new HeaderNotFoundError("Ogg Page");

            var idPacket = page.Packets[0];

            // Determine the codec from the identification packet.
            if (idPacket is [0x01, _, _, _, _, _, _, ..] && Encoding.ASCII.GetString(idPacket, 1, 6) == "vorbis")
                ParseVorbisIdentificationHeader(idPacket, info);
            else if (idPacket.Length >= 19 && Encoding.ASCII.GetString(idPacket, 0, 8) == "OpusHead")
                ParseOpusIdentificationHeader(idPacket, info);
            else
                return new UnsupportedFormatError("Ogg stream is not a supported Vorbis or Opus stream.");

            // The second page should contain the comment header (tags).
            page = await ReadNextPageAsync(stream);
            if (page is { Packets.Count: > 0 } && options.ReadTags)
            {
                var commentPacket = page.Packets[0];
                switch (info.CodecName)
                {
                    case "Vorbis" when commentPacket.Length > 7 && commentPacket[0] == 0x03:
                    {
                        using var memStream = new MemoryStream(commentPacket);
                        memStream.Position = 7; // Skip packet type and "vorbis"
                        var vorbisResult = new VorbisCommentReader().Read(memStream, memStream.Length - 7, options);
                        if(vorbisResult.IsFailure) return Result<SoundFormatInfo>.Fail(vorbisResult.Error!);
                        info.Tags = vorbisResult.Value;
                        break;
                    }
                    case "Opus" when commentPacket.Length > 8 &&
                                     Encoding.ASCII.GetString(commentPacket, 0, 8) == "OpusTags":
                    {
                        using var memStream = new MemoryStream(commentPacket);
                        memStream.Position = 8; // Skip "OpusTags"
                        var vorbisResult = new VorbisCommentReader().Read(memStream, memStream.Length - 8, options);
                        if(vorbisResult.IsFailure) return Result<SoundFormatInfo>.Fail(vorbisResult.Error!);
                        info.Tags = vorbisResult.Value;
                        break;
                    }
                }
            }

            // Duration Calculation
            if (options.DurationAccuracy == DurationAccuracy.AccurateScan)
            {
                var lastGranulePosition = await FindLastPageGranuleAsync(stream);
                if (lastGranulePosition > 0)
                {
                    // For Opus, the granule position is always based on a 48 kHz clock, for Vorbis it's the PCM sample number.
                    var divisor = info.CodecName == "Opus" ? 48000.0 : info.SampleRate;
                    if (divisor > 0)
                        info.Duration = TimeSpan.FromSeconds(lastGranulePosition / divisor);
                }
            }

            if (info.Duration.TotalSeconds > 0) info.Bitrate = (int)(stream.Length * 8 / info.Duration.TotalSeconds);
        }
        catch (EndOfStreamException ex)
        {
            return new CorruptChunkError("Ogg Page", "File is truncated or a page segment is incorrect.", ex);
        }
        
        return info;
    }

    private void ParseVorbisIdentificationHeader(byte[] packet, SoundFormatInfo info)
    {
        using var reader = new BinaryReader(new MemoryStream(packet));
        reader.BaseStream.Position = 7; // Skip packet type and "vorbis"
        reader.ReadUInt32(); // Version
        info.ChannelCount = reader.ReadByte();
        info.SampleRate = reader.ReadInt32();
        reader.ReadInt32(); // Max Bitrate
        var nominalBitrate = reader.ReadInt32();
        reader.ReadInt32(); // Min Bitrate
        info.Bitrate = nominalBitrate > 0 ? nominalBitrate : info.Bitrate;
        info.CodecName = "Vorbis";
    }

    private void ParseOpusIdentificationHeader(byte[] packet, SoundFormatInfo info)
    {
        // Packet starts with "OpusHead" (8 bytes)
        info.CodecName = "Opus";
        info.ChannelCount = packet[9];
        // Input Sample Rate is a 32-bit little-endian integer at offset 12
        info.SampleRate = BitConverter.ToInt32(packet, 12);
    }

    private async Task<OggPage?> ReadNextPageAsync(Stream stream)
    {
        var page = new OggPage();

        var fourByteBuffer = new byte[4];
        while (await stream.ReadAsync(fourByteBuffer.AsMemory(0, 1)) > 0)
        {
            if (fourByteBuffer[0] == 'O')
                if (await stream.ReadAsync(fourByteBuffer.AsMemory(1, 3)) == 3 &&
                    fourByteBuffer[1] == 'g' && fourByteBuffer[2] == 'g' && fourByteBuffer[3] == 'S')
                {
                    stream.Position -= 4;
                    break;
                }

            if (stream.Position >= stream.Length) return null;
        }

        var headerBytes = new byte[27];
        if (await stream.ReadAsync(headerBytes.AsMemory(0, 27)) < 27) return null;

        page.GranulePosition = BitConverter.ToInt64(headerBytes, 6);
        int pageSegments = headerBytes[26];
        var segmentTable = new byte[pageSegments];
        await stream.ReadExactlyAsync(segmentTable, 0, pageSegments);

        foreach (var segmentLength in segmentTable)
        {
            var packetBytes = new byte[segmentLength];
            await stream.ReadExactlyAsync(packetBytes, 0, segmentLength);
            page.Packets.Add(packetBytes);
        }

        return page;
    }

    private async Task<long> FindLastPageGranuleAsync(Stream stream)
    {
        const int bufferSize = 65536;
        if (stream.Length < bufferSize)
            stream.Seek(0, SeekOrigin.Begin);
        else
            stream.Seek(-bufferSize, SeekOrigin.End);

        var buffer = new byte[bufferSize];
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bufferSize));

        for (var i = bytesRead - 27; i >= 0; i--)
            if (buffer[i] == 'O' && buffer[i + 1] == 'g' && buffer[i + 2] == 'g' && buffer[i + 3] == 'S')
                if ((buffer[i + 5] & 0x04) != 0) // End of stream flag
                    return BitConverter.ToInt64(buffer, i + 6);

        return -1;
    }

    private class OggPage
    {
        public long GranulePosition { get; set; }
        public List<byte[]> Packets { get; } = [];
    }
}