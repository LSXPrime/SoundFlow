namespace SoundFlow.Security.Models;

/// <summary>
/// Represents a single feature point in an audio fingerprint.
/// </summary>
/// <param name="Hash">The computed hash value representing the relationship between spectral peaks.</param>
/// <param name="TimeOffset">The time offset (in analysis frames) where the anchor point of this hash occurs.</param>
public readonly record struct FingerprintHash(uint Hash, int TimeOffset);