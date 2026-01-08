using System.Security.Cryptography;
using SoundFlow.Abstracts;
using SoundFlow.Providers;
using SoundFlow.Security;
using SoundFlow.Security.Configuration;

namespace SoundFlow.Samples.Security.Encryption;

/// <summary>
/// Encapsulates the logic for encrypting an audio file.
/// </summary>
public static class EncryptionService
{
    /// <summary>
    /// Encrypts a source audio file into a secure SoundFlow container.
    /// Optionally signs the output file if a private key is provided.
    /// </summary>
    /// <param name="engine">The audio engine for decoding.</param>
    /// <param name="sourceFile">The path to the original audio file.</param>
    /// <param name="outputFile">The path to save the encrypted container file.</param>
    /// <param name="secretKey">The 32-byte secret key for encryption.</param>
    /// <param name="privateKey">The PEM-encoded private key. If provided, the output will be digitally signed.</param>
    /// <param name="embedSignature">If true and <paramref name="privateKey"/> is provided, the signature will be embedded in the file header.</param>
    public static async Task EncryptAsync(AudioEngine engine, string sourceFile, string outputFile, byte[] secretKey, string? privateKey = null, bool embedSignature = false)
    {
        Console.WriteLine($"Loading '{sourceFile}' for encryption...");
        await using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read);
        using var provider = new AssetDataProvider(engine, sourceStream);
        
        // Generate a random 12-byte IV (Nonce). This MUST be unique for each encryption operation
        // with the same key to ensure security.
        var iv = RandomNumberGenerator.GetBytes(12);

        var config = new EncryptionConfiguration
        {
            Key = secretKey,
            Iv = iv
        };

        // If a private key is provided, enable digital signing.
        SignatureConfiguration? signConfig = null;
        if (!string.IsNullOrEmpty(privateKey))
        {
            Console.WriteLine(embedSignature ? "Digital Signing enabled (Embedded)." : "Digital Signing enabled (Detached).");
            signConfig = new SignatureConfiguration { PrivateKeyPem = privateKey };
        }
        
        Console.WriteLine($"Streaming encrypted data to container '{outputFile}'...");
        
        // Critical: When signing is enabled, the destination stream must be Readable and Seekable 
        // because the signer needs to read the file hash after encryption.
        // FileAccess.ReadWrite allows this.
        await using var destinationStream = new FileStream(outputFile, FileMode.Create, FileAccess.ReadWrite);
        
        // Pass embedSignature flag to AudioEncryptor
        var signature = await AudioEncryptor.EncryptAsync(provider, destinationStream, config, signConfig, embedSignature);

        // If a detached signature was generated (signature is not null), save it to a sidecar file.
        if (signature != null)
        {
            var sigPath = outputFile + ".sig";
            await File.WriteAllTextAsync(sigPath, signature);
            Console.WriteLine($"Detached signature saved to '{sigPath}'.");
        }
        else if (signConfig != null && embedSignature)
        {
             Console.WriteLine("Signature embedded successfully.");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Encryption complete.");
        Console.ResetColor();
    }
}