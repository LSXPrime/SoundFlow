namespace SoundFlow.Security.Models;

/// <summary>
/// Represents a raw candidate match retrieved from the fingerprint store.
/// </summary>
/// <param name="TrackId">The unique identifier of the track in the database.</param>
/// <param name="TrackTimeOffset">The time offset (in frames) where this hash occurs in the source track.</param>
public readonly record struct FingerprintMatchCandidate(string TrackId, int TrackTimeOffset);