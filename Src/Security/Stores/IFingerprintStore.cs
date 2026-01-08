using SoundFlow.Security.Models;

namespace SoundFlow.Security.Stores;

/// <summary>
/// Defines the contract for a fingerprint storage backend.
/// Implement this interface to store fingerprints in a database (SQL, NoSQL, Redis, etc.).
/// </summary>
public interface IFingerprintStore
{
    /// <summary>
    /// Stores a computed fingerprint in the database.
    /// </summary>
    /// <param name="fingerprint">The fingerprint to store.</param>
    Task InsertAsync(AudioFingerprint fingerprint);

    /// <summary>
    /// Queries the database for tracks containing the specified hash.
    /// </summary>
    /// <param name="hash">The hash to look up.</param>
    /// <returns>A list of candidates containing the track ID and the time offset where the hash occurs.</returns>
    Task<List<FingerprintMatchCandidate>> QueryHashAsync(uint hash);
}