namespace SoundFlow.Samples.Security.Authentication;

/// <summary>
/// This sample program demonstrates ECDSA-P384 file signing and verification.
/// </summary>
public static class Program
{
    public static async Task Main()
    {
        Console.WriteLine("--- SoundFlow Digital Signature Sample ---");
        Console.WriteLine("This tool demonstrates ECDSA-P384 file signing and verification.\n");

        var keysDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Keys");
        var privateKeyPath = Path.Combine(keysDir, "private.pem");
        var publicKeyPath = Path.Combine(keysDir, "public.pem");

        try
        {
            // Phase 1: Key Management
            KeyManagementService.EnsureKeysExist(privateKeyPath, publicKeyPath);
            var privateKey = await File.ReadAllTextAsync(privateKeyPath);
            var publicKey = await File.ReadAllTextAsync(publicKeyPath);

            // Phase 2: File Selection
            var inputPath = await FilePreparationService.GetTargetFileAsync();
            if (inputPath is null) return;

            // Phase 3: Signing
            var signedSuccessfully = await SigningService.SignFileAndSaveSignatureAsync(inputPath, privateKey);
            if (!signedSuccessfully) return;

            // Phase 4: Verification (Positive)
            Console.WriteLine("\n--- Phase 4: Verification (Clean State) ---");
            await VerificationService.VerifyFileAsync(inputPath, publicKey);

            // Phase 5: Verification (Tampered)
            await TamperingService.TamperAndVerifyAsync(inputPath, 
                () => VerificationService.VerifyFileAsync(inputPath, publicKey));

        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nA critical, unhandled error occurred: {ex.Message}");
            Console.ResetColor();
        }
        
        Console.WriteLine("\n=== End of Sample ===");
    }
}