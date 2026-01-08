using SoundFlow.Backends.MiniAudio;
using SoundFlow.Security.Stores;

// This sample program demonstrates the core workflow of the Audio Fingerprinting system.
// 1. It indexes a full-length audio track provided by the user.
// 2. It attempts to identify a short, re-encoded clip provided by the user against the index.
// 3. It reports the result, including the matched Track ID, confidence, and time offset.

namespace SoundFlow.Samples.Security.Fingerprinting;

public static class Program
{
    public static async Task Main()
    {
        Console.WriteLine("--- SoundFlow Audio Fingerprinting Test ---");
        Console.WriteLine();

        // Setup: Prompt user for required audio files.
        var originalTrackFile = GetExistingFilePath("Enter the path to the full audio track you want to index (e.g., original.wav):");
        Console.WriteLine();
        var queryClipFile = GetExistingFilePath("Enter the path to the short audio clip you want to identify (e.g., clip.wav):");
        Console.WriteLine();

        // The AudioEngine is required for decoding audio files.
        using var engine = new MiniAudioEngine();

        // For this example, we use an in-memory store. In a real application,
        // you would implement IFingerprintStore to connect to a persistent database.
        var fingerprintStore = new InMemoryFingerprintStore();

        // Phase 1: Indexing
        try
        {
            var originalTrackId = await IndexingService.IndexTrackAsync(originalTrackFile, engine, fingerprintStore);
            Console.WriteLine($"Indexed track '{originalTrackId}' successfully.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred during indexing: {ex.Message}");
            Console.ResetColor();
            return;
        }

        Console.WriteLine();

        // Phase 2: Identification
        try
        {
            var result = await IdentificationService.IdentifyClipAsync(queryClipFile, engine, fingerprintStore);

            // Phase 3: Verification
            Console.WriteLine("--- Verification ---");
            if (result is not null && result.TrackId == Path.GetFileName(originalTrackFile))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("SUCCESS: The clip was correctly identified.");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILURE: The clip was not identified or matched the wrong track.");
            }
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred during identification: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine("\nFingerprinting sample finished.");
    }

    /// <summary>
    /// Prompts the user for a file path and loops until a valid, existing file is provided.
    /// </summary>
    /// <param name="promptMessage">The message to display to the user.</param>
    /// <returns>A validated, existing file path.</returns>
    private static string GetExistingFilePath(string promptMessage)
    {
        Console.WriteLine(promptMessage);
        while (true)
        {
            Console.Write("> ");
            var filePath = Console.ReadLine()?.Replace("\"", "");

            if (string.IsNullOrWhiteSpace(filePath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("File path cannot be empty. Please try again.");
                Console.ResetColor();
                continue;
            }

            if (!File.Exists(filePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: The file '{filePath}' was not found. Please check the path and try again.");
                Console.ResetColor();
                continue;
            }

            return filePath;
        }
    }
}