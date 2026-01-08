using SoundFlow.Abstracts;
using SoundFlow.Providers;
using SoundFlow.Security;
using SoundFlow.Security.Configuration;
using SoundFlow.Structs;

namespace SoundFlow.Samples.Security.OwnershipWatermarking;

/// <summary>
/// Encapsulates the logic for extracting an ownership watermark from an audio file.
/// </summary>
public static class WatermarkExtractionService
{
    /// <summary>
    /// Attempts to extract a secret message from an audio file.
    /// </summary>
    /// <param name="engine">The audio engine for processing.</param>
    /// <param name="inputFile">The path to the (potentially modified) watermarked audio file.</param>
    /// <param name="config">The watermark configuration used for embedding.</param>
    /// <returns>A result object containing the extracted message or an error.</returns>
    public static async Task<Result<string>> ExtractAsync(AudioEngine engine, string inputFile, WatermarkConfiguration config)
    {
        Console.WriteLine($"Attempting to extract watermark from '{inputFile}'...");
        await using var stream = new FileStream(inputFile, FileMode.Open);
        using var provider = new AssetDataProvider(engine, stream);

        var result = await AudioWatermarker.ExtractOwnershipWatermarkAsync(provider, config);

        if (result.IsSuccess)
        {
            Console.WriteLine("Extraction successful.");
            Console.WriteLine($"  -> Extracted Payload: '{result.Value}'");
        }
        else
        {
            Console.WriteLine("Extraction failed.");
            Console.WriteLine($"  -> Reason: {result.Error?.Message}");
        }

        return result;
    }
}