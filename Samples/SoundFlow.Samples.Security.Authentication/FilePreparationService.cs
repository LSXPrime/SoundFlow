namespace SoundFlow.Samples.Security.Authentication;

/// <summary>
/// Handles the selection and creation of the target file for signing and verification.
/// </summary>
public static class FilePreparationService
{
    /// <summary>
    /// Prompts the user to select an input file, or creates a dummy file if no input is given.
    /// </summary>
    /// <returns>The path to the selected or created file.</returns>
    public static async Task<string?> GetTargetFileAsync()
    {
        Console.WriteLine("\n--- Phase 2: File Selection ---");
        Console.Write("Enter path to input audio file (leave empty to create a dummy file): ");
        var inputPath = Console.ReadLine()?.Trim().Replace("\"", "");

        if (string.IsNullOrEmpty(inputPath))
        {
            inputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dummy_audio.wav");
            await CreateDummyFileAsync(inputPath);
            Console.WriteLine($"Created dummy file at: {inputPath}");
        }
        else if (!File.Exists(inputPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: File not found at {inputPath}");
            Console.ResetColor();
            return null;
        }

        return inputPath;
    }

    /// <summary>
    /// Creates a 1MB file with random data.
    /// </summary>
    private static async Task CreateDummyFileAsync(string path)
    {
        var buffer = new byte[1024 * 1024]; // 1MB
        Random.Shared.NextBytes(buffer);
        await File.WriteAllBytesAsync(path, buffer);
    }
}