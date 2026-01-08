using SoundFlow.Backends.MiniAudio;
using SoundFlow.Security.Configuration;

namespace SoundFlow.Samples.Security.IntegrityWatermarking;

/// <summary>
/// This sample program demonstrates the fragile Integrity Watermarking feature.
/// 1. It embeds an integrity watermark into a source WAV file.
/// 2. It verifies that the clean, watermarked file passes the integrity check.
/// 3. It creates a tampered version of the file by zeroing out a small section of audio.
/// 4. It verifies that the tampered file FAILS the integrity check.
/// </summary>
public static class Program
{
    public static async Task Main()
    {
        Console.WriteLine("--- SoundFlow Integrity Watermarking Test (Fragile) ---");
        Console.WriteLine();

        var originalFile = GetExistingFilePath("Enter the path to the source audio file (e.g., original.wav):");

        var tempDir = Path.Combine(Path.GetTempPath(), "SoundFlowSample-Integrity");
        Directory.CreateDirectory(tempDir);
        var watermarkedFile = Path.Combine(tempDir, "integrity-watermarked.wav");
        var tamperedFile = Path.Combine(tempDir, "integrity-tampered.wav");

        using var engine = new MiniAudioEngine();
        var config = new WatermarkConfiguration { IntegrityBlockSize = 8192 };
        var success = true;

        try
        {
            Console.WriteLine("\n--- Phase 1: Embedding Watermark ---");
            await IntegrityEmbeddingService.EmbedAsync(engine, originalFile, watermarkedFile, config);

            Console.WriteLine("\n--- Phase 2: Verifying Clean File ---");
            var cleanResult = IntegrityVerificationService.VerifyFileIntegrity(engine, watermarkedFile, config);
            if (cleanResult)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("SUCCESS: Clean file passed integrity check as expected.");
            }
            else
            {
                success = false;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILURE: Clean file FAILED integrity check unexpectedly.");
            }

            Console.ResetColor();

            Console.WriteLine("\n--- Phase 3: Creating Tampered File ---");
            TamperingService.TamperFileByZeroingData(watermarkedFile, tamperedFile);

            Console.WriteLine("\n--- Phase 4: Verifying Tampered File ---");
            var tamperedResult = IntegrityVerificationService.VerifyFileIntegrity(engine, tamperedFile, config);
            if (!tamperedResult)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("SUCCESS: Tampered file FAILED integrity check as expected.");
            }
            else
            {
                success = false;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILURE: Tampered file PASSED integrity check unexpectedly.");
            }

            Console.ResetColor();
        }
        catch (Exception ex)
        {
            success = false;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nAn unexpected error occurred: {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            Console.WriteLine("\n--- Final Result ---");
            Console.WriteLine(success ? "All integrity tests passed." : "One or more integrity tests failed.");

            Console.WriteLine("\nCleaning up temporary files...");
            if (File.Exists(watermarkedFile)) File.Delete(watermarkedFile);
            if (File.Exists(tamperedFile)) File.Delete(tamperedFile);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir);
        }

        Console.WriteLine("Integrity watermarking sample finished.");
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