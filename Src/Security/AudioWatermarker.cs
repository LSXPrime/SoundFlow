using System.Buffers;
using System.Text;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Security.Analyzers;
using SoundFlow.Security.Configuration;
using SoundFlow.Security.Modifiers;
using SoundFlow.Security.Payloads;
using SoundFlow.Structs;

namespace SoundFlow.Security;

/// <summary>
/// Provides high-level methods to apply and extract watermarks.
/// </summary>
public static class AudioWatermarker
{
    private const int BufferSize = 8192;

    /// <summary>
    /// Embeds a text ownership watermark into an audio source and writes the result to a destination stream.
    /// The output format is 32-bit Float WAV.
    /// </summary>
    /// <param name="source">The source audio provider.</param>
    /// <param name="destination">The destination stream (must be writable and seekable to update headers).</param>
    /// <param name="text">The text to embed.</param>
    /// <param name="config">Configuration options.</param>
    /// <exception cref="ArgumentException">Thrown if the destination stream does not support seeking.</exception>
    public static void EmbedOwnershipWatermark(ISoundDataProvider source, Stream destination, string text,
        WatermarkConfiguration config)
    {
        if (!destination.CanSeek || !destination.CanWrite)
            throw new ArgumentException(
                "Destination stream must be writable and seekable to generate a valid WAV container.",
                nameof(destination));

        var format = new AudioFormat
        {
            SampleRate = source.SampleRate,
            Channels = source.FormatInfo?.ChannelCount ?? 2,
            Format = SampleFormat.F32,
            Layout = AudioFormat.GetLayoutFromChannels(source.FormatInfo?.ChannelCount ?? 2)
        };

        var payload = new TextPayload(text);
        var embedder = new OwnershipWatermarkEmbedModifier(payload, config) { Enabled = true };
        using var writer = new BinaryWriter(destination, Encoding.ASCII, true);

        // 1. Write WAV Header placeholders
        var startPos = destination.Position;
        WriteWavHeader(writer, format.SampleRate, format.Channels, 0);

        var dataChunkSizePos = destination.Position - 4; // Position of 'data' chunk size

        // 2. Stream Process
        if (source.CanSeek) source.Seek(0);

        var floatBuffer = ArrayPool<float>.Shared.Rent(BufferSize);
        var byteBuffer = ArrayPool<byte>.Shared.Rent(BufferSize * sizeof(float));

        long totalDataBytes = 0;

        try
        {
            var spanFloat = floatBuffer.AsSpan();
            while (true)
            {
                var samplesRead = source.ReadBytes(spanFloat);
                if (samplesRead == 0) break;

                var validSlice = spanFloat[..samplesRead];

                // Apply watermark
                embedder.Process(validSlice, format.Channels);

                // Convert to bytes (F32 raw)
                Buffer.BlockCopy(floatBuffer, 0, byteBuffer, 0, samplesRead * 4);

                destination.Write(byteBuffer, 0, samplesRead * 4);
                totalDataBytes += samplesRead * 4;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(floatBuffer);
            ArrayPool<byte>.Shared.Return(byteBuffer);
        }

        // 3. Update Header Sizes
        var originalPos = destination.Position;

        // Patch 'data' chunk size
        destination.Seek(dataChunkSizePos, SeekOrigin.Begin);
        writer.Write((uint)totalDataBytes);

        // Patch 'RIFF' chunk size (File Length - 8)
        destination.Seek(startPos + 4, SeekOrigin.Begin);
        writer.Write((uint)(destination.Length - startPos - 8));

        // Restore position
        destination.Seek(originalPos, SeekOrigin.Begin);
    }

    /// <summary>
    /// Wrapper to embed a watermark directly to a file path.
    /// </summary>
    /// <param name="source">The source audio provider.</param>
    /// <param name="destinationPath">The output file path.</param>
    /// <param name="text">The text to embed.</param>
    /// <param name="config">Configuration options.</param>
    public static void EmbedOwnershipWatermarkToFile(ISoundDataProvider source, string destinationPath, string text,
        WatermarkConfiguration config)
    {
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
        EmbedOwnershipWatermark(source, fileStream, text, config);
    }

    private static void WriteWavHeader(BinaryWriter writer, int sampleRate, int channels, int dataSize)
    {
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize); // Placeholder
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16); // Chunk size
        writer.Write((short)3); // Format 3 = IEEE Float
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 4); // ByteRate
        writer.Write((short)(channels * 4)); // BlockAlign
        writer.Write((short)32); // BitsPerSample
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize); // Placeholder
    }

    /// <summary>
    /// Attempts to extract a text payload from a watermarked audio source.
    /// </summary>
    public static async Task<Result<string>> ExtractOwnershipWatermarkAsync(ISoundDataProvider source,
        WatermarkConfiguration config)
    {
        var format = new AudioFormat
        {
            SampleRate = source.SampleRate,
            Channels = source.FormatInfo?.ChannelCount ?? 2,
            Format = source.SampleFormat
        };

        var extractor = new OwnershipWatermarkExtractAnalyzer(format, config);
        var payloadResult = new TaskCompletionSource<string>();

        // Hook up event
        extractor.PayloadExtracted += (bits) =>
        {
            var payload = new TextPayload();
            var text = payload.FromBits(bits) as string;
            payloadResult.TrySetResult(text ?? string.Empty);
        };

        // Process audio
        if (source.CanSeek) source.Seek(0);
        var buffer = ArrayPool<float>.Shared.Rent(BufferSize);

        try
        {
            while (true)
            {
                var read = source.ReadBytes(buffer);
                if (read == 0) break;

                extractor.Process(buffer.AsSpan(0, read), format.Channels);

                if (payloadResult.Task.IsCompleted) break;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }

        extractor.Finish();

        return payloadResult.Task.IsCompleted
            ? Result<string>.Ok(await payloadResult.Task)
            : Result<string>.Fail(new Error("No watermark detected or payload incomplete."));
    }
}