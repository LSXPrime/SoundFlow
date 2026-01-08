using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Metadata.Models;
using SoundFlow.Providers;
using SoundFlow.Security.Configuration;
using SoundFlow.Security.Modifiers;
using SoundFlow.Structs;

namespace SoundFlow.Samples.Security.IntegrityWatermarking;

/// <summary>
/// Encapsulates the logic for embedding a fragile integrity watermark.
/// </summary>
public static class IntegrityEmbeddingService
{
    /// <summary>
    /// Embeds an integrity watermark into a source audio file and saves the result.
    /// </summary>
    /// <param name="engine">The audio engine for processing.</param>
    /// <param name="sourceFile">The path to the original audio file.</param>
    /// <param name="outputFile">The path to save the watermarked file.</param>
    /// <param name="config">The watermark configuration to use.</param>
    public static async Task EmbedAsync(AudioEngine engine, string sourceFile, string outputFile, WatermarkConfiguration config)
    {
        await using var stream = new FileStream(sourceFile, FileMode.Open);
        using var provider = new AssetDataProvider(engine, stream);
        Console.WriteLine($"Embedding integrity watermark into '{sourceFile}'...");

        var format = new AudioFormat
        {
            SampleRate = provider.SampleRate,
            Channels = provider.FormatInfo?.ChannelCount ?? 2
        };
        var embedder = new IntegrityWatermarkEmbedModifier(config);

        var samples = new float[provider.Length];
        provider.ReadBytes(samples);

        embedder.Process(samples, format.Channels);
        
        await SaveWavAsync(engine, outputFile, samples, provider.FormatInfo);
        Console.WriteLine($"Saved watermarked file to '{outputFile}'.");
    }

    /// <summary>
    /// Saves a float array of audio samples to a WAV file.
    /// </summary>
    private static async Task SaveWavAsync(AudioEngine engine, string filePath, float[] samples, SoundFormatInfo? formatInfo)
    {
        var format = new AudioFormat
        {
            SampleRate = formatInfo?.SampleRate ?? 48000,
            Channels = formatInfo?.ChannelCount ?? 2,
            Format = SampleFormat.F32,
            Layout = AudioFormat.GetLayoutFromChannels(formatInfo?.ChannelCount ?? 2)
        };

        await using var stream = new FileStream(filePath, FileMode.Create);
        using var encoder = engine.CreateEncoder(stream, "wav", format);
        encoder.Encode(samples);
    }
}