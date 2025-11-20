using System.Text;
using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Readers.Tags;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Readers.Format;

internal class WavReader : BaseSoundFormatReader
{
    public override async Task<Result<SoundFormatInfo>> ReadAsync(Stream stream, ReadOptions options)
    {
        var info = new SoundFormatInfo
        {
            FormatName = "WAV", 
            FormatIdentifier = "wav",
            IsLossless = true, 
            BitrateMode = BitrateMode.CBR
        };
        
        try
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, true);
            if (new string(reader.ReadChars(4)) != "RIFF")
                return new HeaderNotFoundError("RIFF");
            reader.ReadInt32(); // ChunkSize
            info.ContainerVersion = new string(reader.ReadChars(4));
            if (info.ContainerVersion != "WAVE")
                return new CorruptChunkError("RIFF", "Invalid RIFF type, expected WAVE.");

            long dataSize = 0;
            var cuePoints = new Dictionary<uint, ulong>();
            var cueLabels = new Dictionary<uint, string>();
            bool fmtChunkFound = false;

            while (stream.Position < stream.Length - 8)
            {
                var chunkId = new string(reader.ReadChars(4));
                var chunkSize = reader.ReadInt32();
                var chunkEnd = stream.Position + chunkSize;

                // Ensure chunk doesn't go past the end of the stream
                if (chunkEnd > stream.Length)
                    return new CorruptChunkError(chunkId, "Chunk size exceeds file boundaries.");

                switch (chunkId)
                {
                    case "fmt ":
                        ParseFmtChunk(reader, info);
                        fmtChunkFound = true;
                        break;
                    case "data":
                        dataSize = chunkSize;
                        break;
                    case "cue ":
                        if (options.ReadCueSheet) ParseCueChunk(reader, chunkSize, cuePoints);
                        break;
                    case "LIST":
                        // Can contain CUE labels ('adtl') or metadata ('INFO')
                        ParseListChunk(reader, chunkSize, cueLabels, info, options);
                        break;
                    case "id3 ":
                        if (options.ReadTags)
                        {
                            var chunkBytes = reader.ReadBytes(chunkSize);
                            using var chunkStream = new MemoryStream(chunkBytes);
                            // The Id3V2Reader expects a full tag block, including the header.
                            var id3Result = await new Id3V2Reader().ReadAsync(chunkStream, options);
                            if (id3Result.IsFailure) return Result<SoundFormatInfo>.Fail(id3Result.Error!);
                            
                            var (tags, _) = id3Result.Value;
                            if (tags != null) info.Tags = tags;
                        }
                        break;
                }
                
                // Seek to the start of the next chunk, accounting for odd-sized chunk padding
                stream.Position = chunkSize % 2 == 1 ? chunkEnd + 1 : chunkEnd;
            }

            if (!fmtChunkFound || dataSize == 0)
                return new CorruptChunkError("fmt/data", "Essential format or data chunks are missing.");

            info.Duration = TimeSpan.FromSeconds(dataSize / ((double)info.Bitrate / 8));

            if (options.ReadCueSheet && cuePoints.Count > 0) AssembleCueSheet(info, cuePoints, cueLabels);
        }
        catch (EndOfStreamException ex)
        {
            return new CorruptChunkError("File", "File is truncated or a chunk size is incorrect.", ex);
        }

        return info;
    }

    private void ParseFmtChunk(BinaryReader reader, SoundFormatInfo info)
    {
        var audioFormat = reader.ReadInt16();
        info.CodecName = audioFormat == 1 ? "PCM" : $"Compressed ({audioFormat})";
        info.IsLossless = audioFormat == 1;
        info.ChannelCount = reader.ReadInt16();
        info.SampleRate = reader.ReadInt32();
        var byteRate = reader.ReadInt32();
        info.Bitrate = byteRate * 8;
        reader.ReadInt16(); // block align
        info.BitsPerSample = reader.ReadInt16();
    }

    private void ParseCueChunk(BinaryReader reader, int chunkSize, Dictionary<uint, ulong> cuePoints)
    {
        var numCuePoints = reader.ReadUInt32();
        for (var i = 0; i < numCuePoints && reader.BaseStream.Position < reader.BaseStream.Position + chunkSize - 4; i++)
        {
            var id = reader.ReadUInt32();
            var position = reader.ReadUInt32();
            reader.ReadBytes(16); // Skip data_chunk_id, chunk_start, block_start, sample_offset
            cuePoints[id] = position;
        }
    }

    private void ParseListChunk(BinaryReader reader, int chunkSize, Dictionary<uint, string> cueLabels, SoundFormatInfo info, ReadOptions options)
    {
        var listEnd = reader.BaseStream.Position + chunkSize;
        var listType = new string(reader.ReadChars(4));

        switch (listType)
        {
            case "adtl" when options.ReadCueSheet:
                ParseAdtlSubChunks(reader, listEnd, cueLabels);
                break;
            case "INFO" when options.ReadTags:
                info.Tags ??= new SoundTags();
                ParseInfoSubChunks(reader, listEnd, info.Tags);
                break;
        }
    }

    private void ParseAdtlSubChunks(BinaryReader reader, long listEnd, Dictionary<uint, string> cueLabels)
    {
        while (reader.BaseStream.Position < listEnd)
        {
            var subChunkId = new string(reader.ReadChars(4));
            var subChunkSize = reader.ReadInt32();
            var subChunkEnd = reader.BaseStream.Position + subChunkSize;

            if (subChunkId == "labl")
            {
                var cuePointId = reader.ReadUInt32();
                var labelBytes = reader.ReadBytes(subChunkSize - 4);
                var nullTerminator = Array.IndexOf(labelBytes, (byte)0);
                var label = Encoding.ASCII.GetString(labelBytes, 0, nullTerminator > -1 ? nullTerminator : labelBytes.Length);
                cueLabels[cuePointId] = label;
            }

            reader.BaseStream.Position = subChunkSize % 2 == 1 ? subChunkEnd + 1 : subChunkEnd;
        }
    }
    
    private void ParseInfoSubChunks(BinaryReader reader, long listEnd, SoundTags tags)
    {
        while (reader.BaseStream.Position < listEnd)
        {
            var subChunkId = new string(reader.ReadChars(4));
            var subChunkSize = reader.ReadInt32();
            var subChunkEnd = reader.BaseStream.Position + subChunkSize;

            var valueBytes = reader.ReadBytes(subChunkSize);
            var nullTerminator = Array.IndexOf(valueBytes, (byte)0);
            var value = Encoding.UTF8.GetString(valueBytes, 0, nullTerminator > -1 ? nullTerminator : valueBytes.Length);

            switch (subChunkId)
            {
                case "INAM": tags.Title = value; break;
                case "IART": tags.Artist = value; break;
                case "IPRD": tags.Album = value; break;
                case "IGNR": tags.Genre = value; break;
                case "ICRD": // Creation Date, just the year
                    if (uint.TryParse(value.AsSpan(0, 4), out var year)) tags.Year = year;
                    break;
                case "ITRK":
                    if (uint.TryParse(value, out var track)) tags.TrackNumber = track;
                    break;
            }
            
            reader.BaseStream.Position = subChunkSize % 2 == 1 ? subChunkEnd + 1 : subChunkEnd;
        }
    }

    private void AssembleCueSheet(SoundFormatInfo info, Dictionary<uint, ulong> cuePoints, Dictionary<uint, string> cueLabels)
    {
        info.Cues = new CueSheet();
        foreach (var kvp in cuePoints)
            info.Cues.Add(new CuePoint
            {
                Id = kvp.Key,
                PositionSamples = kvp.Value,
                Label = cueLabels.TryGetValue(kvp.Key, out var label) ? label : $"Track {kvp.Key}",
                StartTime = TimeSpan.FromSeconds((double)kvp.Value / info.SampleRate)
            });
        info.Cues.Sort();
    }
}