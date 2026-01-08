using System.Text;
using SoundFlow.Interfaces;
using SoundFlow.Metadata.Models;
using SoundFlow.Providers;
using SoundFlow.Security.Configuration;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Security;

/// <summary>
/// Provides static methods to automatically determine the optimal watermarking configuration
/// for a given audio source and payload. This "tuner" simulates embedding and extracting
/// at various parameters to find a balance between robustness and inaudibility.
/// </summary>
public static class WatermarkTuner
{
    /// <summary>
    /// Defines the discrete spread factor levels to test, ordered from most robust (and slowest)
    /// to least robust (and fastest). A higher spread factor disperses the watermark signal
    /// over more audio frames, increasing its resilience to damage.
    /// </summary>
    private static readonly int[] SpreadLevels = [16384, 8192, 4096, 2048];

    /// <summary>
    /// Asynchronously analyzes an audio source to find the most robust watermarking configuration.
    /// It iterates through predefined spread factors and strength levels, simulating an embed/extract
    /// cycle at strategic audio positions to verify the payload's integrity.
    /// </summary>
    /// <param name="source">The audio data provider to analyze.</param>
    /// <param name="payload">The string data to be embedded in the watermark.</param>
    /// <param name="secretKey">The secret key used for encryption and hashing within the watermark.</param>
    /// <param name="applySafetyMargin">If true, the tuner will apply a safety margin to the final strength.</param>
    /// <returns>
    /// A task that resolves to a <see cref="WatermarkConfiguration"/> object containing the
    /// recommended parameters. If no suitable configuration is found, returns a safe default.
    /// </returns>
    public static async Task<WatermarkConfiguration> TuneConfigurationAsync(
        ISoundDataProvider source,
        string payload,
        string secretKey,
        bool applySafetyMargin = false)
    {
        Log.Info("WatermarkTuner: Starting comprehensive auto-tuning process.");

        var channels = source.FormatInfo?.ChannelCount ?? 1;
        if (channels == 0) channels = 1;

        foreach (var spread in SpreadLevels)
        {
            Log.Debug($"Tuner: Testing Spread Factor {spread}");

            // 1. Calculate required duration for the watermark at the current spread factor.
            var payloadBytes = Encoding.UTF8.GetByteCount(payload);
            // The total bits include CRC16 (16), payload length (32), and the payload itself.
            var totalBits = 16 + 32 + (payloadBytes * 8); 
            var framesNeeded = totalBits * spread;
            
            // Add a 5-second buffer to prevent the watermark from ending abruptly at the file's edge.
            var bufferFrames = (int)(source.SampleRate * 5.0);
            var totalFramesNeeded = framesNeeded + bufferFrames;

            var fileLengthFrames = source.Length / channels;
            if (totalFramesNeeded > fileLengthFrames)
            {
                var requiredSeconds = (double)totalFramesNeeded / source.SampleRate;
                var fileSeconds = (double)fileLengthFrames / source.SampleRate;
                Log.Debug($"Tuner: Payload too long for spread factor {spread}. Requires {requiredSeconds:F1}s, file is {fileSeconds:F1}s. Skipping.");
                continue;
            }

            // 2. Identify strategic locations in the audio file to test embedding.
            var candidates = GetCandidateOffsets(source, totalFramesNeeded, channels);
            
            // 3. Test various strength levels at each candidate location.
            const float startStrength = 0.02f;
            const float maxStrength = 0.12f; 
            const float step = 0.02f;

            for (var s = startStrength; s <= maxStrength + 0.001f; s += step)
            {
                var config = new WatermarkConfiguration
                {
                    Key = secretKey,
                    SpreadFactor = spread,
                    Strength = s
                };

                foreach (var startFrame in candidates)
                {
                    var sliceSamples = ReadSlice(source, startFrame, totalFramesNeeded);
                    var watermarkedSlice = EmbedToMemory(sliceSamples, source.FormatInfo, payload, config);
                    
                    // Simulate a volume reduction attack to test watermark resilience.
                    ApplyVolumeAttack(watermarkedSlice, 0.75f);
                    
                    var result = await ExtractFromMemory(watermarkedSlice, source.FormatInfo, config);

                    if (result.IsSuccess && result.Value == payload)
                    {
                        // Success! Apply a safety margin to the strength to account for real-world distortions.
                        var margin = 1f;
                        if (applySafetyMargin)
                        {
                            margin = s switch
                            {
                                <= 0.04f => 1.4f,
                                <= 0.08f => 1.2f,
                                _ => 1.1f
                            };
                        }
                        
                        var finalStrength = (float)Math.Round(s * margin, 3);
                        if (finalStrength > 0.14f) finalStrength = 0.14f;

                        config.Strength = finalStrength;

                        Log.Info($"Tuner: Resolved optimal configuration at position {(float)startFrame / source.SampleRate:F1}s. Spread={spread}, BaseStrength={s:F3}, FinalStrength={finalStrength:F3}");
                        return config;
                    }
                }
            }
            Log.Debug($"Tuner: Spread factor {spread} failed at all candidate positions and strengths.");
        }

        Log.Warning("Tuner: Auto-tuning failed to find a robust configuration. Defaulting to Spread=16384, Strength=0.10");
        return new WatermarkConfiguration
        {
            Key = secretKey,
            SpreadFactor = 16384,
            Strength = 0.10f
        };
    }

    /// <summary>
    /// Identifies a list of strategic starting frame offsets within the audio source for testing.
    /// </summary>
    /// <param name="source">The audio data provider.</param>
    /// <param name="frameCount">The total number of frames the watermark requires.</param>
    /// <param name="channels">The number of audio channels.</param>
    /// <returns>A list of integer offsets, each representing a starting frame index.</returns>
    private static List<int> GetCandidateOffsets(ISoundDataProvider source, int frameCount, int channels)
    {
        var results = new List<int>();
        var fileLengthFrames = source.Length / channels;
        var validRegion = fileLengthFrames - frameCount;

        if (validRegion <= 0) return [0];

        // Candidate 1: The very beginning of the file.
        results.Add(0);

        // Candidate 2: 10 seconds in, to skip potential silent intros or fade-ins.
        var offset10S = Math.Min(validRegion, (int)(source.SampleRate * 10.0));
        if (offset10S > source.SampleRate) results.Add(offset10S);

        // Candidate 3: The most acoustically "dense" region for maximum resilience.
        var denseOffset = FindDenseAudioSlice(source, frameCount, channels);
        if (denseOffset != -1 && Math.Abs(denseOffset - 0) > source.SampleRate * 5) // Ensure it's not too close to the start
        {
            results.Add(denseOffset);
        }

        return results.Distinct().ToList();
    }

    /// <summary>
    /// Scans the audio source to find the slice that is least likely to contain silence.
    /// Watermarks are generally more robust when embedded in louder, more complex audio segments.
    /// </summary>
    /// <param name="source">The audio data provider.</param>
    /// <param name="frameCount">The size of the slice to evaluate, in frames.</param>
    /// <param name="channels">The number of audio channels.</param>
    /// <returns>The starting frame index of the "densest" slice, or -1 if none could be determined.</returns>
    private static int FindDenseAudioSlice(ISoundDataProvider source, int frameCount, int channels)
    {
        var fileLengthFrames = source.Length / channels;
        var validRegion = fileLengthFrames - frameCount;
        if (validRegion <= 0) return 0;

        var bestStart = -1;
        var minSilenceCount = int.MaxValue;
        const float silenceThreshold = 0.003f;
        
        const int candidatesToCheck = 5;
        var candidateStride = validRegion / candidatesToCheck;
        if (candidateStride == 0) candidateStride = 1;

        var buffer = new float[frameCount * channels];

        for (var i = 0; i < validRegion; i += candidateStride)
        {
            if(source.CanSeek) source.Seek(i * channels);
            var read = source.ReadBytes(buffer);
            if (read < buffer.Length) break;

            var silenceCount = 0;
            for (var k = 0; k < read; k += 200) 
            {
                if (Math.Abs(buffer[k]) < silenceThreshold) silenceCount++;
            }

            if (silenceCount < minSilenceCount)
            {
                minSilenceCount = silenceCount;
                bestStart = i;
                // If we find a slice with no silence at all, it's a perfect candidate.
                if (minSilenceCount == 0) break;
            }
        }
        
        return bestStart;
    }

    /// <summary>
    /// Reads a specific slice of audio data from a provider into an in-memory buffer.
    /// </summary>
    /// <param name="source">The audio data provider.</param>
    /// <param name="startFrameIndex">The frame index to start reading from.</param>
    /// <param name="frameCount">The number of frames to read.</param>
    /// <returns>A float array containing the requested audio samples.</returns>
    private static float[] ReadSlice(ISoundDataProvider source, int startFrameIndex, int frameCount)
    {
        var channels = source.FormatInfo?.ChannelCount ?? 1;
        var absoluteSampleOffset = startFrameIndex * channels;
        
        if (source.CanSeek) source.Seek(absoluteSampleOffset);

        var totalSamplesToRead = frameCount * channels;
        var buffer = new float[totalSamplesToRead];
        var samplesRead = source.ReadBytes(buffer);

        // Return a potentially smaller array if the read operation hit the end of the stream.
        return samplesRead < buffer.Length ? buffer.AsSpan(0, samplesRead).ToArray() : buffer;
    }

    /// <summary>
    /// Helper method to embed a watermark into an in-memory sample buffer.
    /// </summary>
    /// <param name="sourceSamples">The raw audio samples to modify.</param>
    /// <param name="info">The format information for the audio.</param>
    /// <param name="text">The payload text to embed.</param>
    /// <param name="config">The watermarking configuration to use.</param>
    /// <returns>A new float array containing the watermarked audio samples.</returns>
    private static float[] EmbedToMemory(float[] sourceSamples, SoundFormatInfo? info, string text, WatermarkConfiguration config)
    {
        using var sourceProvider = new RawDataProvider(sourceSamples)
        {
            FormatInfo = info
        };
        using var memoryStream = new MemoryStream();
        
        // 1. Write watermarked WAV to MemoryStream
        AudioWatermarker.EmbedOwnershipWatermark(sourceProvider, memoryStream, text, config);
        
        // 2. Read back as raw float samples
        const int headerSize = 44; // AudioWatermarker uses a deterministic writer: 44 bytes header.
        
        if (memoryStream.Length <= headerSize) return [];

        var dataLengthBytes = memoryStream.Length - headerSize;
        var floatCount = dataLengthBytes / sizeof(float);
        var resultBuffer = new float[floatCount];
        
        memoryStream.Position = headerSize;
        
        // Read directly into byte buffer then block copy to float[]
        var byteBuffer = new byte[dataLengthBytes];
        var read = memoryStream.Read(byteBuffer, 0, (int)dataLengthBytes);
        
        Buffer.BlockCopy(byteBuffer, 0, resultBuffer, 0, read);
        
        return resultBuffer;
    }

    /// <summary>
    /// Simulates a simple volume reduction attack on an in-memory sample buffer.
    /// This is used during tuning to test watermark resilience.
    /// </summary>
    /// <param name="samples">The audio sample buffer to modify in-place.</param>
    /// <param name="volume">The volume multiplier (e.g., 0.75 for 75% volume).</param>
    private static void ApplyVolumeAttack(float[] samples, float volume)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= volume;
        }
    }

    /// <summary>
    /// Helper method to extract a watermark from an in-memory sample buffer.
    /// </summary>
    /// <param name="watermarkedSamples">The raw audio samples containing a watermark.</param>
    /// <param name="info">The format information for the audio.</param>
    /// <param name="config">The watermarking configuration to use for extraction.</param>
    /// <returns>A task resolving to a <see cref="Result{T}"/> containing the extracted payload if successful.</returns>
    private static async Task<Result<string>> ExtractFromMemory(float[] watermarkedSamples, SoundFormatInfo? info, WatermarkConfiguration config)
    {
        using var provider = new RawDataProvider(watermarkedSamples)
        {
            FormatInfo = info
        };
        return await AudioWatermarker.ExtractOwnershipWatermarkAsync(provider, config);
    }
}