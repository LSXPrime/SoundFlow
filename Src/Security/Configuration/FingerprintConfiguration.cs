namespace SoundFlow.Security.Configuration;

/// <summary>
/// Configuration settings for the content fingerprint analyzer.
/// </summary>
public class FingerprintConfiguration
{
    /// <summary>
    /// Gets or sets the size of the FFT window. Must be a power of 2.
    /// Default is 2048.
    /// </summary>
    public int FftSize { get; set; } = 2048;

    /// <summary>
    /// Gets or sets the overlap factor between analysis frames.
    /// Default is 2 (50% overlap).
    /// </summary>
    public int OverlapFactor { get; set; } = 2;

    /// <summary>
    /// Gets or sets the minimum frequency to consider for peaks (Hz).
    /// Default is 300Hz.
    /// </summary>
    public float MinFrequency { get; set; } = 300.0f;

    /// <summary>
    /// Gets or sets the maximum frequency to consider for peaks (Hz).
    /// Default is 5000Hz.
    /// </summary>
    public float MaxFrequency { get; set; } = 5000.0f;

    /// <summary>
    /// Gets or sets the size of the target zone in time (frames) for combinatorial hashing.
    /// Reducing this reduces hash density.
    /// Default is 3 frames (reduced from 5 to optimize density).
    /// </summary>
    public int TargetZoneSize { get; set; } = 3;

    /// <summary>
    /// Gets or sets the minimum magnitude required for a spectral peak to be considered.
    /// Normalized range 0.0 to 1.0.
    /// Default is 0.01 (increased from 0.002 to reduce noise hashes).
    /// </summary>
    public double MinPeakMagnitude { get; set; } = 0.01;

    /// <summary>
    /// Gets or sets the multiplier for adaptive thresholding.
    /// A peak must be this many times larger than the local average to be selected.
    /// Default is 2.0 (increased from 1.5 to select only prominent peaks).
    /// </summary>
    public double AdaptiveThresholdMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets the minimum absolute number of aligned hashes required to declare a match.
    /// Default is 25.
    /// </summary>
    public int MinConfidenceThreshold { get; set; } = 25;

    /// <summary>
    /// Gets or sets the minimum relative confidence score (Matched Hashes / Total Query Hashes).
    /// Range 0.0 to 1.0. 
    /// A value of 0.05 (5%) implies that at least 5% of the query's hashes must match the target.
    /// This effectively filters out random collisions in large queries.
    /// </summary>
    public double MinRelativeConfidence { get; set; } = 0.05;
}