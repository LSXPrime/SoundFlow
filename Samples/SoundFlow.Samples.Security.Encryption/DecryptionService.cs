using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Security;
using SoundFlow.Security.Configuration;
using SoundFlow.Structs;

namespace SoundFlow.Samples.Security.Encryption;

/// <summary>
/// Encapsulates the logic for decrypting a SoundFlow container file.
/// </summary>
public static class DecryptionService
{
    /// <summary>
    /// Decrypts a SoundFlow container file and saves the raw audio to a WAV file.
    /// Optionally verifies the file authenticity if a public key is provided.
    /// </summary>
    /// <param name="engine">The audio engine for encoding.</param>
    /// <param name="inputFile">The path to the encrypted container file.</param>
    /// <param name="outputFile">The path to save the decrypted WAV file.</param>
    /// <param name="secretKey">The 32-byte secret key used for encryption.</param>
    /// <param name="publicKey">The PEM-encoded public key. If provided, the file signature (embedded or detached) will be verified.</param>
    public static async Task DecryptAsync(AudioEngine engine, string inputFile, string outputFile, byte[] secretKey,
        string? publicKey = null)
    {
        Console.WriteLine($"Loading encrypted container '{inputFile}'...");

        Result<ISoundDataProvider> providerResult;

        // The provider returned by Decrypt/VerifyAndDecrypt takes ownership of this stream.
        var fileStream = new FileStream(inputFile, FileMode.Open, FileAccess.Read);

        if (!string.IsNullOrEmpty(publicKey))
        {
            Console.WriteLine("Verifying digital signature before decryption...");
            
            // Check for detached signature first
            string? signature = null;
            var sigPath = inputFile + ".sig";
            if (File.Exists(sigPath))
            {
                Console.WriteLine("Found detached signature file.");
                signature = await File.ReadAllTextAsync(sigPath);
            }
            else
            {
                Console.WriteLine("No detached signature found. Checking for embedded signature...");
            }

            var signConfig = new SignatureConfiguration
            {
                PublicKeyPem = publicKey
            };

            // This reads the whole file to verify (handling embedded or detached), then rewinds for decryption.
            providerResult = await AudioEncryptor.VerifyAndDecryptAsync(fileStream, secretKey, signConfig, signature);
        }
        else
        {
            // Standard decryption without verification
            providerResult = AudioEncryptor.Decrypt(fileStream, secretKey);
        }

        if (providerResult.IsFailure)
        {
            // If provider creation fails, we must manually dispose the stream.
            await fileStream.DisposeAsync();
            throw new InvalidOperationException($"Failed to decrypt/verify file: {providerResult.Error?.Message}");
        }

        // The provider now owns the fileStream and will dispose it when the provider is disposed.
        using var provider = providerResult.Value!;

        Console.WriteLine($"Saving decrypted audio to '{outputFile}'...");

        // Save the decrypted stream to a WAV file
        await using var outStream = new FileStream(outputFile, FileMode.Create);
        var wavFormat = new AudioFormat
        {
            SampleRate = provider.SampleRate,
            Channels = provider.FormatInfo?.ChannelCount ?? 2,
            Format = SampleFormat.F32,
            Layout = AudioFormat.GetLayoutFromChannels(provider.FormatInfo?.ChannelCount ?? 2)
        };

        using var encoder = engine.CreateEncoder(outStream, "wav", wavFormat);

        // Stream copy loop: read from the decrypting provider and write to the WAV encoder.
        var buffer = new float[4096];
        while (true)
        {
            var read = provider.ReadBytes(buffer);
            if (read == 0) break;
            encoder.Encode(buffer.AsSpan(0, read));
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Decryption complete.");
        Console.ResetColor();
    }
}