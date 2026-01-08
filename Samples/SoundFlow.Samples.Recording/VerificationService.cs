using SoundFlow.Security;
using SoundFlow.Security.Configuration;

namespace SoundFlow.Samples.Recording;

/// <summary>
/// Helper to verify the authenticity of a signed recording.
/// </summary>
public static class VerificationService
{
    public static async Task VerifyRecordingAsync(string filePath, string publicKeyPem)
    {
        Console.WriteLine("\n--- Authenticated Save Verification ---");
        var sigPath = filePath + ".sig";

        if (!File.Exists(sigPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Signature file not found. Verification impossible.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine("Signature file found.");
        Console.WriteLine("Verifying integrity and authenticity...");

        try
        {
            var signature = await File.ReadAllTextAsync(sigPath);
            var config = new SignatureConfiguration { PublicKeyPem = publicKeyPem };

            var result = await FileAuthenticator.VerifyFileAsync(filePath, signature, config);

            if (result.IsSuccess && result.Value)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("SUCCESS: The recording is authentic and has not been tampered with.");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILURE: Verification failed! The file may have been modified.");
                if (result.IsFailure) Console.WriteLine($"Details: {result.Error?.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error during verification: {ex.Message}");
        }
        finally
        {
            Console.ResetColor();
        }
    }
}