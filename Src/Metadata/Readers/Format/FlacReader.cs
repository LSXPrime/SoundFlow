using System.Text;
using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Readers.Tags;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Readers.Format;

internal class FlacReader : BaseSoundFormatReader
{
    public override Task<Result<SoundFormatInfo>> ReadAsync(Stream stream, ReadOptions options)
    {
        var info = new SoundFormatInfo
        {
            FormatName = "FLAC",
            CodecName = "FLAC",
            FormatIdentifier = "flac",
            IsLossless = true
        };
        
        try
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, true);
            if (new string(reader.ReadChars(4)) != "fLaC")
                return Task.FromResult(Result<SoundFormatInfo>.Fail(new HeaderNotFoundError("fLaC marker")));

            bool isLastBlock;
            bool streamInfoFound = false;
            do
            {
                var header = reader.ReadByte();
                isLastBlock = (header & 0x80) != 0;
                var blockType = (byte)(header & 0x7F);
                var blockSize = Read24BitInt(reader);
                var blockEnd = stream.Position + blockSize;

                if (blockEnd > stream.Length)
                    return Task.FromResult(Result<SoundFormatInfo>.Fail(new CorruptChunkError("Metadata Block", "Block size exceeds file boundaries.")));

                switch (blockType)
                {
                    case 0: // STREAMINFO
                        ParseStreamInfo(reader, info);
                        streamInfoFound = true;
                        break;
                    case 4: // VORBIS_COMMENT
                        if (options.ReadTags)
                        {
                            var vorbisResult = new VorbisCommentReader().Read(stream, blockSize, options);
                            if(vorbisResult.IsFailure) return Task.FromResult(Result<SoundFormatInfo>.Fail(vorbisResult.Error!));
                            info.Tags = vorbisResult.Value;
                        }
                        break;
                    case 6: // PICTURE
                        if (options is { ReadTags: true, ReadAlbumArt: true } && info.Tags?.AlbumArt == null)
                        {
                            info.Tags ??= new SoundTags();
                            var pictureResult = new VorbisCommentReader().ParsePictureBlock(stream);
                            if (pictureResult.IsFailure) return Task.FromResult(Result<SoundFormatInfo>.Fail(pictureResult.Error!));
                            info.Tags.AlbumArt = pictureResult.Value;
                        }

                        break;
                    case 5: // CUESHEET
                        if (options.ReadCueSheet) info.Cues = ParseCueSheet(reader, info.SampleRate);
                        break;
                }

                stream.Position = blockEnd;
            } while (!isLastBlock && stream.Position < stream.Length);

            if (!streamInfoFound)
                return Task.FromResult(Result<SoundFormatInfo>.Fail(new CorruptChunkError("STREAMINFO", "The mandatory STREAMINFO block is missing.")));

            if (info.Duration.TotalSeconds > 0)
            {
                var audioDataSize = stream.Length - stream.Position;
                info.Bitrate = (int)(audioDataSize * 8 / info.Duration.TotalSeconds);
                info.BitrateMode = BitrateMode.VBR;
            }
        }
        catch (EndOfStreamException ex)
        {
            return Task.FromResult(Result<SoundFormatInfo>.Fail(new CorruptChunkError("Metadata Block", "File is truncated or a block size is incorrect.", ex)));
        }

        return Task.FromResult(Result<SoundFormatInfo>.Ok(info));
    }

    private void ParseStreamInfo(BinaryReader reader, SoundFormatInfo info)
    {
        reader.ReadUInt16(); // min block size
        reader.ReadUInt16(); // max block size
        reader.ReadBytes(3); // min frame size
        reader.ReadBytes(3); // max frame size

        var sampleInfo = reader.ReadBytes(8);

        var sampleRate = ((long)sampleInfo[0] << 12) | ((long)sampleInfo[1] << 4) | (uint)((sampleInfo[2] & 0xF0) >> 4);
        var channels = ((sampleInfo[2] & 0x0E) >> 1) + 1;
        var bitsPerSample = (((sampleInfo[2] & 0x01) << 4) | ((sampleInfo[3] & 0xF0) >> 4)) + 1;
        var totalSamples = (((long)sampleInfo[3] & 0x0F) << 32) | ((long)sampleInfo[4] << 24) |
                           ((long)sampleInfo[5] << 16) | ((long)sampleInfo[6] << 8) | sampleInfo[7];

        info.SampleRate = (int)sampleRate;
        info.ChannelCount = channels;
        info.BitsPerSample = bitsPerSample;
        if (totalSamples > 0 && sampleRate > 0) info.Duration = TimeSpan.FromSeconds((double)totalSamples / sampleRate);
    }

    private CueSheet ParseCueSheet(BinaryReader reader, int sampleRate)
    {
        var cueSheet = new CueSheet();
        reader.ReadBytes(392); // Skip Media catalog ID, lead-in samples, is_cd

        int trackCount = reader.ReadByte();
        reader.ReadBytes(258); // Reserved

        for (var i = 0; i < trackCount; i++)
        {
            var offset = reader.ReadUInt64();
            uint number = reader.ReadByte();
            reader.ReadBytes(12); // ISRC
            reader.ReadBytes(2); // type/pre-emphasis
            reader.ReadBytes(13); // reserved
            cueSheet.Add(new CuePoint
            {
                Id = number,
                PositionSamples = offset,
                Label = $"Track {number:D2}",
                StartTime = TimeSpan.FromSeconds((double)offset / sampleRate)
            });
        }

        return cueSheet;
    }

    private int Read24BitInt(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(3);
        if (bytes.Length < 3) throw new EndOfStreamException();
        return (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
    }
}