using SoundFlow.Backends.MiniAudio;
using SoundFlow.Security.Utils;

namespace SoundFlow.Samples.Security.Encryption;

/// <summary>
/// This sample program demonstrates the Content Encryption feature.
/// 1. It encrypts an original audio file into a custom container file (.sfa).
/// 2. It decrypts the container file back into raw audio.
/// 3. It saves the decrypted audio to a new WAV file.
/// 4. It verifies that the original and decrypted files are bit-for-bit identical.
/// 5. It plays the encrypted container in real-time.
/// </summary>
public static class Program
{
    // For a real application, this key should be managed securely (e.g., from a key vault or secure store).
    // It must be 32 bytes (256 bits).
    private static readonly byte[] SecretKey = "MySuperSecure32ByteEncryptionKey"u8.ToArray();
    
    public static async Task Main()
    {
        Console.WriteLine("--- SoundFlow Content Encryption & Authentication Test ---");
        Console.WriteLine();
        
        var originalFile = GetExistingFilePath("Enter path to the source audio file (e.g., original.wav):");
        
        Console.Write("Enable Authenticated Encryption (Digital Signatures)? (y/N): ");
        var enableAuth = Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ?? false;

        string? privateKey = null;
        string? publicKey = null;
        var embedSignature = false;

        if (enableAuth)
        {
            Console.Write("Embed Signature in File Header? (y/N): ");
            embedSignature = Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ?? false;
            Console.WriteLine("Generating ephemeral ECDSA-P384 key pair for this session...");
            var keys = SignatureKeyGenerator.Generate();
            privateKey = keys.PrivateKeyPem;
            publicKey = keys.PublicKeyPem;
            Console.WriteLine("Keys generated.");
        }
        
        var tempDir = Path.Combine(Path.GetTempPath(), "SoundFlowSample-Encryption");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);
        var encryptedFile = Path.Combine(tempDir, "encrypted.sfa");
        var decryptedFile = Path.Combine(tempDir, "decrypted.wav");
        
        using var engine = new MiniAudioEngine();
        var success = true;

        try
        {
            // Phase 1: Encrypt
            Console.WriteLine("\n--- Phase 1: Encrypting File ---");
            await EncryptionService.EncryptAsync(engine, originalFile, encryptedFile, SecretKey, privateKey, embedSignature);

            // Phase 2: Decrypt
            Console.WriteLine("\n--- Phase 2: Decrypting File ---");
            await DecryptionService.DecryptAsync(engine, encryptedFile, decryptedFile, SecretKey, publicKey);

            // Phase 3: Verify
            Console.WriteLine("\n--- Phase 3: Verifying Audio Integrity ---");
            var areIdentical = await VerificationService.AreFilesIdenticalAsync(engine, originalFile, decryptedFile);
            if (areIdentical)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("SUCCESS: Original and decrypted files are identical.");
            }
            else
            {
                success = false;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILURE: Original and decrypted files DO NOT match.");
            }
            Console.ResetColor();

            // Phase 4: Real-time Playback
            if (success)
            {
                Console.WriteLine("\n--- Phase 4: Real-time Encrypted Playback ---");
                await PlaybackService.PlayEncryptedStreamAsync(engine, encryptedFile, SecretKey, publicKey);
            }
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
            Console.WriteLine(success ? "Encryption tests passed." : "Encryption tests failed.");
            
            Console.WriteLine("\nCleaning up temporary files...");
            if (File.Exists(encryptedFile)) File.Delete(encryptedFile);
            if (File.Exists(encryptedFile + ".sig")) File.Delete(encryptedFile + ".sig");
            if (File.Exists(decryptedFile)) File.Delete(decryptedFile);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir);
        }
        
        Console.WriteLine("Encryption sample finished.");
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