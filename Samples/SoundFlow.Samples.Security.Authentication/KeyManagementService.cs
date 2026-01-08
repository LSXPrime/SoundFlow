using SoundFlow.Security.Utils;

namespace SoundFlow.Samples.Security.Authentication;

/// <summary>
/// Manages the creation, storage, and retrieval of cryptographic keys for the sample.
/// </summary>
public static class KeyManagementService
{
    /// <summary>
    /// Ensures that private and public key files exist, prompting the user to generate them if they don't,
    /// or if the user requests regeneration.
    /// </summary>
    /// <param name="privateKeyPath">The path to the private key file.</param>
    /// <param name="publicKeyPath">The path to the public key file.</param>
    public static void EnsureKeysExist(string privateKeyPath, string publicKeyPath)
    {
        var keysDir = Path.GetDirectoryName(privateKeyPath);
        Directory.CreateDirectory(keysDir!);

        Console.WriteLine("--- Phase 1: Key Management ---");
        if (File.Exists(privateKeyPath) && File.Exists(publicKeyPath))
        {
            Console.WriteLine($"Found existing keys in {keysDir}");
            Console.Write("Do you want to generate NEW keys? This will invalidate existing signatures. (y/N): ");
            var response = Console.ReadKey();
            Console.WriteLine();
            if (response.Key == ConsoleKey.Y)
            {
                GenerateAndSaveKeys(privateKeyPath, publicKeyPath);
            }
        }
        else
        {
            Console.WriteLine("No keys found.");
            GenerateAndSaveKeys(privateKeyPath, publicKeyPath);
        }
    }

    /// <summary>
    /// Generates a new ECDSA-P384 key pair and saves it to the specified files.
    /// </summary>
    private static void GenerateAndSaveKeys(string privPath, string pubPath)
    {
        Console.WriteLine("Generating new ECDSA-P384 Key Pair...");
        var keys = SignatureKeyGenerator.Generate();
        
        File.WriteAllText(privPath, keys.PrivateKeyPem);
        File.WriteAllText(pubPath, keys.PublicKeyPem);
        
        Console.WriteLine($"Keys saved to {Path.GetDirectoryName(privPath)}");
    }
}