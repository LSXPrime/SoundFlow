using SoundFlow.Backends.MiniAudio;

namespace SoundFlow.Samples.Security.OwnershipWatermarking;

/// <summary>
/// This sample program demonstrates the robust Ownership Watermarking feature.
/// 1. It auto-tunes watermark settings for a source WAV file.
/// 2. It embeds a secret text message into the file.
/// 3. It saves the watermarked audio.
/// 4. It simulates a "distribution attack" by altering the volume of the watermarked file.
/// 5. It then attempts to extract the secret message from the modified file.
/// 6. It verifies if the extracted message matches the original secret.
/// </summary>
public static class Program
{
    private const string SecretMessage = "SoundFlow Ownership Test - Property of LSXPrime";
    private const string SecretKey = "MySuperSecretKey123!";
    
    public static async Task Main()
    {
        Console.WriteLine("--- SoundFlow Ownership Watermarking Test (Robust) ---");
        Console.WriteLine();
        
        var originalFile = GetExistingFilePath("Enter the path to the source audio file you want to watermark (e.g., original.wav):");
        
        // Define temporary file paths in a temporary directory
        var tempDir = Path.Combine(Path.GetTempPath(), "SoundFlowSample");
        Directory.CreateDirectory(tempDir);
        var watermarkedFile = Path.Combine(tempDir, "watermarked.wav");
        var modifiedFile = Path.Combine(tempDir, "watermarked-modified.wav");
        
        using var engine = new MiniAudioEngine();

        try
        {
            // Phase 0: Auto-Tuning
            Console.WriteLine("\n--- Phase 0: Auto-Tuning Configuration ---");
            var config = await WatermarkTuningService.TuneAsync(engine, originalFile, SecretMessage, SecretKey);

            // Phase 1: Embedding
            Console.WriteLine("\n--- Phase 1: Embedding Watermark ---");
            await WatermarkEmbeddingService.EmbedAsync(engine, originalFile, watermarkedFile, SecretMessage, config);

            // Phase 2: Simulate Attack
            Console.WriteLine("\n--- Phase 2: Simulating Modification Attack (Volume Change) ---");
            await AttackSimulationService.SimulateVolumeChangeAsync(engine, watermarkedFile, modifiedFile, 0.75f);

            // Phase 3: Extraction & Verification
            Console.WriteLine("\n--- Phase 3: Extracting Watermark from Modified File ---");
            var result = await WatermarkExtractionService.ExtractAsync(engine, modifiedFile, config);

            Console.WriteLine("\n--- Verification ---");
            if (result is { IsSuccess: true, Value: SecretMessage })
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("SUCCESS: The secret message was correctly extracted from the modified file.");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILURE: The extracted message did not match the original secret.");
                Console.WriteLine($"  -> Expected: '{SecretMessage}'");
                Console.WriteLine($"  -> Got:      '{(result.IsSuccess ? result.Value : result.Error?.Message)}'");
            }
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nAn unexpected error occurred: {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            // Cleanup generated files
            Console.WriteLine("\nCleaning up temporary files...");
            if (File.Exists(watermarkedFile)) File.Delete(watermarkedFile);
            if (File.Exists(modifiedFile)) File.Delete(modifiedFile);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir);
            Console.WriteLine("Watermarking sample finished.");
        }
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