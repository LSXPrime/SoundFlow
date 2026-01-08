using SoundFlow.Abstracts;
using SoundFlow.Providers;
using SoundFlow.Security;
using SoundFlow.Security.Models;
using SoundFlow.Security.Stores;

namespace SoundFlow.Samples.Security.Fingerprinting;

/// <summary>
/// Encapsulates the logic for identifying audio clips.
/// </summary>
public static class IdentificationService
{
    /// <summary>
    /// Loads a query clip, runs identification against the store, and prints the result.
    /// </summary>
    /// <param name="filePath">The path to the audio clip to identify.</param>
    /// <param name="engine">The audio engine for decoding.</param>
    /// <param name="store">The fingerprint store to query against.</param>
    /// <returns>The result of the identification process.</returns>
    public static async Task<FingerprintResult?> IdentifyClipAsync(string filePath, AudioEngine engine, IFingerprintStore store)
    {
        Console.WriteLine($"--- Identifying Clip: {filePath} ---");

        using var provider = new StreamDataProvider(engine, new FileStream(filePath, FileMode.Open, FileAccess.Read));

        Console.WriteLine("Identifying clip against the store...");
        var result = await AudioIdentifier.IdentifyAsync(provider, store);

        Console.WriteLine("\n--- Identification Result ---");
        if (result is { IsSuccess: true, Value: not null })
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Match Found!");
            Console.WriteLine($"  -> Track ID:       {result.Value.TrackId}");
            Console.WriteLine($"  -> Confidence:     {result.Value.Confidence} aligned hashes");
            Console.WriteLine($"  -> Time Offset:    The clip starts at approximately {result.Value.MatchTimeSeconds:F2} seconds into the original track.");
            Console.WriteLine($"  -> Processing Time: {result.Value.ProcessingTime.TotalMilliseconds:F2} ms");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No match found in the database.");
        }

        Console.ResetColor();
        Console.WriteLine("-------------------------");

        return null;
    }
}