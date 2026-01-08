using SoundFlow.Abstracts;
using SoundFlow.Providers;
using SoundFlow.Security.Analyzers;
using SoundFlow.Security.Configuration;
using SoundFlow.Structs;

namespace SoundFlow.Samples.Security.IntegrityWatermarking;

/// <summary>
/// Encapsulates the logic for verifying a fragile integrity watermark.
/// </summary>
public static class IntegrityVerificationService
{
    /// <summary>
    /// Verifies the integrity of a watermarked audio file.
    /// </summary>
    /// <param name="engine">The audio engine for processing.</param>
    /// <param name="filePath">The path to the file to verify.</param>
    /// <param name="config">The watermark configuration used for embedding.</param>
    /// <returns>True if the file is intact; false if an integrity violation is detected.</returns>
    public static bool VerifyFileIntegrity(AudioEngine engine, string filePath, WatermarkConfiguration config)
    {
        Console.WriteLine($"Verifying integrity of '{filePath}'...");
        using var stream = new FileStream(filePath, FileMode.Open);
        using var provider = new AssetDataProvider(engine, stream);        
        var format = new AudioFormat { SampleRate = provider.SampleRate, Channels = provider.FormatInfo?.ChannelCount ?? 2 };

        var verifier = new IntegrityWatermarkVerifyAnalyzer(format, config);
        var violationDetected = false;

        verifier.IntegrityViolationDetected += blockIndex =>
        {
            violationDetected = true;
            Console.WriteLine($"  -> Integrity violation detected at block: {blockIndex}");
        };

        var buffer = new float[16384]; // 16KB buffer
        while (true)
        {
            var read = provider.ReadBytes(buffer);
            if (read == 0) break;
            
            // The verifier needs a span of the actual data read
            verifier.Process(buffer.AsSpan(0, read), format.Channels);
            
            // Stop checking immediately after the first failure for efficiency
            if (violationDetected) break;
        }
        
        return !violationDetected;
    }
}