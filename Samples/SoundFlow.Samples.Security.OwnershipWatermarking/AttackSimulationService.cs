using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Metadata.Models;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SoundFlow.Samples.Security.OwnershipWatermarking;

/// <summary>
/// Encapsulates the logic for simulating an attack on a watermarked audio file.
/// </summary>
public static class AttackSimulationService
{
    /// <summary>
    /// Loads a watermarked audio file, modifies its volume, and saves it to a new file.
    /// </summary>
    /// <param name="engine">The audio engine for processing.</param>
    /// <param name="inputFile">The path to the watermarked audio file.</param>
    /// <param name="outputFile">The path to save the modified audio file.</param>
    /// <param name="volumeMultiplier">The factor by which to multiply the audio samples' amplitude.</param>
    public static async Task SimulateVolumeChangeAsync(AudioEngine engine, string inputFile, string outputFile, float volumeMultiplier)
    {
        Console.WriteLine($"Loading '{inputFile}', adjusting volume by {(volumeMultiplier - 1.0f) * 100:F0}%, and saving to '{outputFile}'.");
        await using var stream = new FileStream(inputFile, FileMode.Open);
        using var provider = new AssetDataProvider(engine, stream);

        // Read all samples
        var samples = new float[provider.Length];
        provider.ReadBytes(samples);

        // Modify volume
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= volumeMultiplier;
        }

        await SaveWavAsync(engine, outputFile, samples, provider.FormatInfo);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Attack simulation complete.");
        Console.ResetColor();
    }

    /// <summary>
    /// Saves a float array of audio samples to a WAV file.
    /// </summary>
    private static async Task SaveWavAsync(AudioEngine engine, string filePath, float[] samples, SoundFormatInfo? formatInfo)
    {
        var format = new AudioFormat
        {
            SampleRate = formatInfo?.SampleRate ?? 48000,
            Channels = formatInfo?.ChannelCount ?? 2,
            Format = SampleFormat.F32,
            Layout = AudioFormat.GetLayoutFromChannels(formatInfo?.ChannelCount ?? 2)
        };

        await using var stream = new FileStream(filePath, FileMode.Create);
        using var encoder = engine.CreateEncoder(stream, "wav", format);
        encoder.Encode(samples);
    }
}