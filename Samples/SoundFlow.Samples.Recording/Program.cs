using SoundFlow.Backends.MiniAudio;
using SoundFlow.Metadata.Models;
using SoundFlow.Security.Configuration;
using SoundFlow.Security.Utils;
using SoundFlow.Utils;

namespace SoundFlow.Samples.Recording;

public static class Program
{
    public static async Task Main()
    {
        Console.WriteLine("=== SoundFlow Recorder Sample ===");
        Console.WriteLine("Capabilities: Audio Capture, Metadata Embedding, Authenticated Save.\n");

        Log.OnLog += entry => Console.WriteLine(entry); 

        using var engine = new MiniAudioEngine();

        try
        {
            // 1. Device Selection
            var device = DeviceService.SelectInputDevice(engine);
            if (device == null) return;

            // 2. Configuration: Digital Signing
            SignatureConfiguration? signConfig = null;
            string? publicKey = null; // Kept for verification step

            Console.Write("\nEnable Authenticated Save (Digital Signature)? (y/N): ");
            if (IsYes())
            {
                Console.WriteLine("Generating ephemeral ECDSA-P384 keys...");
                var keys = SignatureKeyGenerator.Generate();
                signConfig = new SignatureConfiguration { PrivateKeyPem = keys.PrivateKeyPem };
                publicKey = keys.PublicKeyPem;
                Console.WriteLine("Keys generated. Private key loaded into recorder.");
            }

            // 3. Configuration: Metadata
            SoundTags? tags = null;
            Console.Write("Add Metadata Tags? (y/N): ");
            if (IsYes())
            {
                tags = new SoundTags();
                Console.Write("  Title: ");
                tags.Title = Console.ReadLine() ?? "Recorded Audio";
                Console.Write("  Artist: ");
                tags.Artist = Console.ReadLine() ?? Environment.UserName;
                tags.Year = (uint)DateTime.Now.Year;
            }

            // 4. File Path
            var fileName = $"Rec_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
            var outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

            // 5. Run Recording
            await RecordingService.RecordAsync(engine, device.Value, outputPath, signConfig, tags);

            // 6. Post-Recording Verification (if applicable)
            if (publicKey != null)
            {
                await VerificationService.VerifyRecordingAsync(outputPath, publicKey);
            }

            Console.WriteLine($"\nFile location: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nCritical Error: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine("\nSample finished.");
    }

    private static bool IsYes()
    {
        return Console.ReadLine()?.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase) ?? false;
    }
}