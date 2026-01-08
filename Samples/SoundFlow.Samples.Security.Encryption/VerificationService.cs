using SoundFlow.Abstracts;
using SoundFlow.Providers;

namespace SoundFlow.Samples.Security.Encryption;

/// <summary>
/// Encapsulates the logic for verifying the integrity of audio files.
/// </summary>
public static class VerificationService
{
    /// <summary>
    /// Compares two audio files sample by sample to determine if they are identical.
    /// </summary>
    /// <param name="engine">The audio engine for decoding.</param>
    /// <param name="originalFile">The path to the first audio file.</param>
    /// <param name="decryptedFile">The path to the second audio file.</param>
    /// <returns>True if the files are identical, otherwise false.</returns>
    public static async Task<bool> AreFilesIdenticalAsync(AudioEngine engine, string originalFile, string decryptedFile)
    {
        Console.WriteLine($"Comparing Audio Samples of '{originalFile}' and '{decryptedFile}'...");

        await using var stream1 = new FileStream(originalFile, FileMode.Open);
        using var provider1 = new StreamDataProvider(engine, stream1);

        await using var stream2 = new FileStream(decryptedFile, FileMode.Open);
        using var provider2 = new StreamDataProvider(engine, stream2);

        // Check metadata first as a quick failure point.
        if (provider1.Length != provider2.Length)
        {
            Console.WriteLine($"  -> Sample count mismatch: Original={provider1.Length}, Decrypted={provider2.Length}.");
            return false;
        }

        // Compare samples in chunks for efficiency.
        var buf1 = new float[8192];
        var buf2 = new float[8192];
        long totalRead = 0;

        while (true)
        {
            var read1 = provider1.ReadBytes(buf1);
            var read2 = provider2.ReadBytes(buf2);

            if (read1 != read2)
            {
                Console.WriteLine($"  -> Read length mismatch at sample offset {totalRead}.");
                return false;
            }
            if (read1 == 0) break;

            for (var i = 0; i < read1; i++)
            {
                // Use a small tolerance for floating point comparisons.
                if (Math.Abs(buf1[i] - buf2[i]) > 1e-4f)
                {
                    Console.WriteLine($"  -> Mismatch at sample {totalRead + i}: Original={buf1[i]}, Decrypted={buf2[i]}");
                    return false;
                }
            }
            totalRead += read1;
        }

        return true;
    }
}