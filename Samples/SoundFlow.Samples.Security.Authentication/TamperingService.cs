namespace SoundFlow.Samples.Security.Authentication;

/// <summary>
/// Provides methods to simulate file tampering for verification testing.
/// </summary>
public static class TamperingService
{
    /// <summary>
    /// Creates a backup of a file, tampers with the original, executes a verification action,
    /// and then restores the original file, ensuring cleanup.
    /// </summary>
    /// <param name="filePath">The path to the file to tamper with.</param>
    /// <param name="verificationAction">An async function to execute on the tampered file.</param>
    public static async Task TamperAndVerifyAsync(string filePath, Func<Task> verificationAction)
    {
        Console.WriteLine("\n--- Phase 5: Verification (Tampered State) ---");
        Console.WriteLine("Simulating tampering by modifying one byte in the file...");

        var backupPath = filePath + ".bak";
        File.Copy(filePath, backupPath, true);

        try
        {
            // Tamper with the file by flipping the bits of the first byte.
            await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
            {
                var firstByte = fs.ReadByte();
                if (firstByte != -1)
                {
                    fs.Seek(0, SeekOrigin.Begin);
                    fs.WriteByte((byte)(firstByte ^ 0xFF));
                }
            }

            // Execute the provided verification logic on the now-tampered file.
            await verificationAction();
        }
        finally
        {
            // Ensure the original file is always restored.
            File.Move(backupPath, filePath, true);
            Console.WriteLine("Restored original file.");
        }
    }
}