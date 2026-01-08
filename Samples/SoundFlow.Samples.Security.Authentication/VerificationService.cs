using SoundFlow.Security;
using SoundFlow.Security.Configuration;

namespace SoundFlow.Samples.Security.Authentication;

/// <summary>
/// Encapsulates the logic for verifying the digital signature of a file.
/// </summary>
public static class VerificationService
{
    /// <summary>
    /// Verifies a file's signature against its public key and prints the result to the console.
    /// </summary>
    /// <param name="filePath">The path to the data file.</param>
    /// <param name="publicKeyPem">The public key in PEM format.</param>
    public static async Task VerifyFileAsync(string filePath, string publicKeyPem)
    {
        Console.Write($"Verifying {Path.GetFileName(filePath)}... ");

        var sigPath = filePath + ".sig";
        var verifyConfig = new SignatureConfiguration { PublicKeyPem = publicKeyPem };

        if (!File.Exists(sigPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Signature file missing!");
            Console.ResetColor();
            return;
        }

        var signature = await File.ReadAllTextAsync(sigPath);
        var result = await FileAuthenticator.VerifyFileAsync(filePath, signature, verifyConfig);

        if (result is { IsSuccess: true, Value: true })
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("VALID / AUTHENTIC");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("INVALID / TAMPERED");
            if (result.IsSuccess)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  -> Signature verification failed, well, successfully.");
            }

            if (result.IsFailure) Console.WriteLine($"  -> Error Details: {result.Error?.Message}");
        }

        Console.ResetColor();
    }
}