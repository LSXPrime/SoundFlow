using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Readers.Tags;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Readers.Format;

internal class AacReader : BaseSoundFormatReader
{
    private static readonly int[] SampleRateTable =
    [
        96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350
    ];

    public override async Task<Result<SoundFormatInfo>> ReadAsync(Stream stream, ReadOptions options)
    {
        var info = new SoundFormatInfo
        {
            FormatName = "AAC",
            CodecName = "Advanced Audio Coding",
            FormatIdentifier = "aac",
            BitsPerSample = 0, // Not applicable
            IsLossless = false,
        };
        
        long audioDataStart = 0;
        if (options.ReadTags)
        {
            var id3Reader = new Id3V2Reader();
            var id3Result = await id3Reader.ReadAsync(stream, options);
            if (id3Result.IsFailure) return Result<SoundFormatInfo>.Fail(id3Result.Error!);
            var (tag, tagSize) = id3Result.Value;
            
            info.Tags = tag;
            audioDataStart = tagSize;
        }
        stream.Position = audioDataStart;
        
        var firstFrameHeader = await FindNextFrameHeaderAsync(stream);
        if (firstFrameHeader == null) return new HeaderNotFoundError("AAC ADTS Frame");

        // Get fundamental properties from the first frame
        var parseResult = ParseFrameHeader(firstFrameHeader, out var sampleRate, out var channelConfig, out _);
        if (parseResult.IsFailure) return Result<SoundFormatInfo>.Fail(parseResult.Error!);
        
        info.ChannelCount = channelConfig;
        info.SampleRate = sampleRate;
        
        var audioDataLength = stream.Length - audioDataStart;

        // For VBR/Unknown files, the only truly accurate method is to scan the entire file.
        if (options.DurationAccuracy == DurationAccuracy.AccurateScan)
        {
            stream.Position = audioDataStart;
            var (frameCount, error) = await CountAllFramesAsync(stream, audioDataStart);
            if (error != null) return Result<SoundFormatInfo>.Fail(error);

            if (frameCount > 0 && info.SampleRate > 0)
            {
                // Each AAC frame contains 1024 samples. This is the most accurate way to calculate duration.
                info.Duration = TimeSpan.FromSeconds((double)frameCount * 1024 / info.SampleRate);
                
                // The true average bitrate is the total audio data size divided by the exact duration.
                if (info.Duration.TotalSeconds > 0) info.Bitrate = (int)(audioDataLength * 8 / info.Duration.TotalSeconds);
                info.BitrateMode = BitrateMode.VBR;
            }
        }
        else // FastEstimate
        {
            // Use the bitrate of the first frame to estimate the duration from the file size.
            parseResult = ParseFrameHeader(firstFrameHeader, out _, out _, out var frameLength);
            if (parseResult.IsFailure) return Result<SoundFormatInfo>.Fail(parseResult.Error!);
            var frameDuration = 1024.0 / info.SampleRate;
            info.Bitrate = (int)(frameLength * 8 / frameDuration);
            if (info.Bitrate > 0)
            {
                info.Duration = TimeSpan.FromSeconds(audioDataLength * 8.0 / info.Bitrate);
            }
            info.BitrateMode = BitrateMode.Unknown;
        }

        return info;
    }

    /// <summary>
    /// Scans the stream to count every valid ADTS frame, starting from a specific offset.
    /// </summary>
    private async Task<(long frameCount, IError? error)> CountAllFramesAsync(Stream stream, long startOffset)
    {
        long frameCount = 0;
        stream.Position = startOffset;
        
        while (stream.Position < stream.Length)
        {
            var header = await FindNextFrameHeaderAsync(stream);
            if (header == null) break; 

            var parseResult = ParseFrameHeader(header, out _, out _, out var frameLength);
            if (parseResult.IsFailure) return (0, parseResult.Error);
            if (frameLength <= 7) continue;

            frameCount++;
            
            var nextFramePosition = stream.Position - 7 + frameLength;
            if (nextFramePosition >= stream.Length || nextFramePosition <= stream.Position - 7) break;

            stream.Position = nextFramePosition;
        }
        return (frameCount, null);
    }

    private Result ParseFrameHeader(byte[] header, out int sampleRate, out int channelConfig, out int frameLength)
    {
        sampleRate = 0;
        channelConfig = 0;
        frameLength = 0;
        
        var samplingIndex = (header[2] & 0x3C) >> 2;
        if (samplingIndex >= SampleRateTable.Length)
            return new CorruptFrameError("AAC", "Invalid sample rate index.");

        sampleRate = SampleRateTable[samplingIndex];
        channelConfig = ((header[2] & 0x01) << 2) | ((header[3] & 0xC0) >> 6);
        frameLength = ((header[3] & 0x03) << 11) | (header[4] << 3) | ((header[5] & 0xE0) >> 5);

        if (frameLength < 7) // A frame must be at least as big as its header
            return new CorruptFrameError("AAC", "Invalid frame length (less than header size).");
        
        return Result.Ok();
    }

    internal static bool IsAacFrame(byte[] buffer)
    {
        return buffer is [0xFF, _, ..] && (buffer[1] & 0xF0) == 0xF0;
    }
    
    private async Task<byte[]?> FindNextFrameHeaderAsync(Stream stream)
    {
        var oneByteBuffer = new byte[1];
        var headerBuffer = new byte[7];
        var streamPos = stream.Position;

        while (streamPos < stream.Length - 1)
        {
            stream.Position = streamPos;
            if (await stream.ReadAsync(oneByteBuffer.AsMemory(0, 1)) == 0) return null;
            
            if (oneByteBuffer[0] == 0xFF)
            {
                if (await stream.ReadAsync(oneByteBuffer.AsMemory(0, 1)) == 0) return null;
                
                if ((oneByteBuffer[0] & 0xF0) == 0xF0)
                {
                    // Found a valid sync word. Read the rest of the header.
                    headerBuffer[0] = 0xFF;
                    headerBuffer[1] = oneByteBuffer[0];
                    if (await stream.ReadAsync(headerBuffer.AsMemory(2, 5)) == 5)
                    {
                        return headerBuffer;
                    }
                    return null; // Hit EOF in the middle of a header.
                }
            }
            // If we're here, it wasn't a sync word. Move to the next byte.
            streamPos++;
        }
        return null;
    }
}