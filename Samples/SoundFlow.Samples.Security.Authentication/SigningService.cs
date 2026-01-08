using SoundFlow.Security;
using SoundFlow.Security.Configuration;

namespace SoundFlow.Samples.Security.Authentication;

/// <summary>
/// Encapsulates the logic for digitally signing a file.
/// </summary>
public static class SigningService
{
    /// <summary>
    /// Signs a file using the provided private key and saves the signature to a corresponding .sig file.
    /// </summary>
    /// <param name="inputPath">The path to the file to sign.</param>
    /// <param name="privateKey">The private key in PEM format.</param>
    /// <returns>True if signing was successful, otherwise false.</returns>
    public static async Task<bool> SignFileAndSaveSignatureAsync(string inputPath, string privateKey)
    {
        Console.WriteLine("\n--- Phase 3: Signing ---");
        var sigPath = inputPath + ".sig";
        var signConfig = new SignatureConfiguration { PrivateKeyPem = privateKey };

        Console.Write($"Signing {Path.GetFileName(inputPath)}...");
        
        var signResult = await FileAuthenticator.SignFileAsync(inputPath, signConfig);
        if (signResult.IsSuccess)
        {
            await File.WriteAllTextAsync(sigPath, signResult.Value);
            Console.WriteLine(" Done!");
            Console.WriteLine($"Signature saved to: {sigPath}");
            Console.WriteLine($"Signature (truncated): {signResult.Value?[..30]}...");
            return true;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($" Failed: {signResult.Error?.Message}");
        Console.ResetColor();
        return false;
    }
}