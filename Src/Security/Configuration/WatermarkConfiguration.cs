namespace SoundFlow.Security.Configuration;

/// <summary>
/// Configuration settings for audio watermarking operations.
/// </summary>
public class WatermarkConfiguration
{
    /// <summary>
    /// Gets or sets the secret key used to seed the pseudo-random number generator for spread spectrum watermarking.
    /// Both the embedder and extractor must use the same key.
    /// </summary>
    public string Key { get; set; } = "DefaultSoundFlowKey";

    /// <summary>
    /// Gets or sets the embedding strength (Alpha).
    /// Higher values make the watermark more robust but more audible.
    /// Range: 0.001 (invisible) to 0.1 (audible hiss).
    /// Default is 0.08 for a good balance of robustness and quality.
    /// </summary>
    public float Strength { get; set; } = 0.08f;

    /// <summary>
    /// Gets or sets the spread factor (Chip Rate).
    /// Determines how many audio samples represent a single bit of data.
    /// Higher values drastically increase robustness against noise and compression but decrease data rate.
    /// Default is 16384 (approx 40 bits/sec at 44.1kHz).
    /// </summary>
    public int SpreadFactor { get; set; } = 16384;

    /// <summary>
    /// Gets or sets the block size for integrity watermarking hashing.
    /// </summary>
    public int IntegrityBlockSize { get; set; } = 4096;
}