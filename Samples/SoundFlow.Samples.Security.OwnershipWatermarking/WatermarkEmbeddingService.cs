using SoundFlow.Abstracts;
using SoundFlow.Providers;
using SoundFlow.Security;
using SoundFlow.Security.Configuration;

namespace SoundFlow.Samples.Security.OwnershipWatermarking;

/// <summary>
/// Encapsulates the logic for embedding an ownership watermark into an audio file.
/// </summary>
public static class WatermarkEmbeddingService
{
    /// <summary>
    /// Embeds a secret message into a source audio file and saves the result.
    /// </summary>
    /// <param name="engine">The audio engine for processing.</param>
    /// <param name="sourceFile">The path to the original audio file.</param>
    /// <param name="outputFile">The path to save the watermarked audio file.</param>
    /// <param name="secretMessage">The secret message to embed.</param>
    /// <param name="config">The watermark configuration to use.</param>
    public static async Task EmbedAsync(AudioEngine engine, string sourceFile, string outputFile, string secretMessage, WatermarkConfiguration config)
    {
        await using var stream = new FileStream(sourceFile, FileMode.Open);
        using var provider = new AssetDataProvider(engine, stream);
        Console.WriteLine($"Embedding '{secretMessage}' into '{sourceFile}'...");

        Console.WriteLine($"Saving watermarked audio to '{outputFile}'...");

        await using var watermarkedStream = new FileStream(outputFile, FileMode.Create);
        AudioWatermarker.EmbedOwnershipWatermark(provider, watermarkedStream, secretMessage, config);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Embedding complete.");
        Console.ResetColor();
    }
}