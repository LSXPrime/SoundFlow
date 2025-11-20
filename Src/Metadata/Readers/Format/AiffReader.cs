using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Readers.Tags;
using SoundFlow.Metadata.Utilities;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Readers.Format;

internal class AiffReader : BaseSoundFormatReader
{
    public override async Task<Result<SoundFormatInfo>> ReadAsync(Stream stream, ReadOptions options)
    {
        var info = new SoundFormatInfo
        {
            FormatName = "AIFF",
            FormatIdentifier = "aiff",
            IsLossless = true, 
            BitrateMode = BitrateMode.CBR
        };
        
        try
        {
            using var reader = new BigEndianBinaryReader(stream);
            if (reader.ReadString(4) != "FORM")
                return new HeaderNotFoundError("FORM");
            reader.ReadInt32(); // ChunkSize
            info.ContainerVersion = reader.ReadString(4);
            if (info.ContainerVersion != "AIFF" && info.ContainerVersion != "AIFC")
                return new CorruptChunkError("FORM", "Invalid FORM type, expected AIFF or AIFC.");

            long numSampleFrames = 0;
            long dataSize = 0;

            while (stream.Position < stream.Length)
            {
                // Not enough bytes for a chunk header
                if (stream.Position + 8 > stream.Length) break;

                var chunkId = reader.ReadString(4);
                var chunkSize = reader.ReadInt32();
                var chunkEnd = stream.Position + chunkSize;

                // Ensure chunk doesn't go past the end of the stream
                if (chunkEnd > stream.Length) 
                    return new CorruptChunkError(chunkId, "Chunk size exceeds file boundaries.");

                switch (chunkId)
                {
                    case "COMM":
                    {
                        info.ChannelCount = reader.ReadInt16();
                        numSampleFrames = reader.ReadUInt32();
                        info.BitsPerSample = reader.ReadInt16();
                        info.SampleRate = (int)reader.ReadExtended();
                        if (info.ContainerVersion == "AIFC")
                        {
                            var compressionType = reader.ReadString(4);
                            info.CodecName = compressionType.Trim();
                            info.IsLossless = compressionType is "NONE" or "pcm ";
                        }
                        else
                        {
                            info.CodecName = "PCM";
                        }

                        break;
                    }
                    case "SSND":
                        reader.ReadUInt32(); // offset
                        reader.ReadUInt32(); // blockSize
                        dataSize = chunkSize - 8;
                        break;
                    case "ID3 ":
                        if (options.ReadTags)
                        {
                            var chunkBytes = reader.ReadBytes(chunkSize);
                            using var chunkStream = new MemoryStream(chunkBytes);
                            var id3Result = await new Id3V2Reader().ReadAsync(chunkStream, options);
                            if (id3Result.IsFailure) return Result<SoundFormatInfo>.Fail(id3Result.Error!);
                            
                            var (tags, _) = id3Result.Value;
                            if (tags != null) info.Tags = tags;
                        }

                        break;
                }

                // Seek to next chunk, padding to an even boundary if chunk size is odd
                stream.Position = chunkSize % 2 == 1 ? chunkEnd + 1 : chunkEnd;
            }

            if (info.SampleRate == 0 || numSampleFrames == 0 || info.BitsPerSample == 0)
                return new CorruptChunkError("COMM", "Essential COMM chunk is missing or incomplete.");

            info.Duration = TimeSpan.FromSeconds((double)numSampleFrames / info.SampleRate);
            if (info.Duration.TotalSeconds > 0) info.Bitrate = (int)(dataSize * 8 / info.Duration.TotalSeconds);
        }
        catch (EndOfStreamException ex)
        {
            return new CorruptChunkError("File", "File is truncated or a chunk size is incorrect.", ex);
        }
        
        return info;
    }
}