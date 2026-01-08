using SoundFlow.Abstracts;
using SoundFlow.Providers;
using SoundFlow.Security;
using SoundFlow.Security.Configuration;

namespace SoundFlow.Samples.Security.OwnershipWatermarking;

/// <summary>
/// Encapsulates the logic for auto-tuning watermark configuration.
/// </summary>
public static class WatermarkTuningService
{
    /// <summary>
    /// Analyzes a source audio file to determine the optimal watermark configuration.
    /// </summary>
    /// <param name="engine">The audio engine for processing.</param>
    /// <param name="sourceFile">The path to the original audio file.</param>
    /// <param name="secretMessage">The exact secret message that will be embedded.</param>
    /// <param name="key">The secret key for the watermark.</param>
    /// <returns>An auto-tuned <see cref="WatermarkConfiguration"/> object.</returns>
    public static async Task<WatermarkConfiguration> TuneAsync(AudioEngine engine, string sourceFile, string secretMessage, string key)
    {
        Console.WriteLine($"Analyzing '{sourceFile}' to find optimal watermark settings...");
        
        await using var tuneStream = new FileStream(sourceFile, FileMode.Open);
        using var tuneProvider = new AssetDataProvider(engine, tuneStream);

        // Pass the actual secret message so the tuner knows exactly how much data needs to fit in the test slice duration.
        // Surely you can use a shorter proxy message for more stable starting point
        var config = await WatermarkTuner.TuneConfigurationAsync(tuneProvider, secretMessage, key);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Auto-tuning complete.");
        Console.WriteLine($"  -> Optimal Strength:      {config.Strength}");
        Console.WriteLine($"  -> Optimal Spread Factor: {config.SpreadFactor}");
        Console.ResetColor();

        return config;
    }
}