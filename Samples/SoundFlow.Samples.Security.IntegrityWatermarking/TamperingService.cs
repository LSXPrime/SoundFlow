namespace SoundFlow.Samples.Security.IntegrityWatermarking;

/// <summary>
/// Provides methods to simulate tampering with audio files.
/// </summary>
public static class TamperingService
{
    /// <summary>
    /// Creates a tampered version of an audio file by zeroing out a block of its binary data.
    /// This method operates at the byte level to simulate corruption or malicious editing.
    /// </summary>
    /// <param name="inputFile">The path to the clean, watermarked file.</param>
    /// <param name="outputFile">The path where the tampered file will be saved.</param>
    public static void TamperFileByZeroingData(string inputFile, string outputFile)
    {
        Console.WriteLine($"Creating tampered file '{outputFile}' by zeroing out a data block...");
        var fileBytes = File.ReadAllBytes(inputFile);
        
        // Find a point roughly in the middle of the data chunk to tamper with.
        // We start after the standard WAV header (44 bytes) to avoid corrupting the format itself.
        const int wavHeaderSize = 44;
        if (fileBytes.Length <= wavHeaderSize)
        {
            // The file is too small to tamper with, so just copy it.
            File.Copy(inputFile, outputFile, true);
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("Warning: File is too small to tamper with. Copied as-is.");
            Console.ResetColor();
            return;
        }
        
        var tamperPoint = wavHeaderSize + (fileBytes.Length - wavHeaderSize) / 2;
        const int tamperLength = 1024; // Zero out 1KB of data

        for (var i = 0; i < tamperLength; i++)
        {
            var index = tamperPoint + i;
            if (index < fileBytes.Length)
            {
                fileBytes[index] = 0;
            }
        }
        
        File.WriteAllBytes(outputFile, fileBytes);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Tampering complete.");
        Console.ResetColor();
    }
}