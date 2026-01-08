using SoundFlow.Abstracts;
using SoundFlow.Providers;
using SoundFlow.Security;
using SoundFlow.Security.Stores;

namespace SoundFlow.Samples.Security.Fingerprinting;

/// <summary>
/// Encapsulates the logic for indexing audio tracks.
/// </summary>
public static class IndexingService
{
    /// <summary>
    /// Loads an audio file, generates its fingerprint, and inserts it into the store.
    /// </summary>
    /// <param name="filePath">The path to the audio file to index.</param>
    /// <param name="engine">The audio engine for decoding.</param>
    /// <param name="store">The fingerprint store for persistence.</param>
    /// <returns>The generated Track ID for the indexed file.</returns>
    public static async Task<string> IndexTrackAsync(string filePath, AudioEngine engine, IFingerprintStore store)
    {
        Console.WriteLine($"--- Indexing Track: {filePath} ---");

        using var provider = new StreamDataProvider(engine, new FileStream(filePath, FileMode.Open, FileAccess.Read));

        Console.WriteLine("Generating fingerprint...");
        var fingerprint = AudioIdentifier.GenerateFingerprint(provider);

        // Use the filename as a human-readable TrackId for this example.
        fingerprint.TrackId = Path.GetFileName(filePath);

        Console.WriteLine($"Generated {fingerprint.Hashes.Count} hashes for track '{fingerprint.TrackId}'.");

        Console.WriteLine("Inserting fingerprint into the store...");
        await store.InsertAsync(fingerprint);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Indexing complete.");
        Console.ResetColor();

        return fingerprint.TrackId;
    }
}