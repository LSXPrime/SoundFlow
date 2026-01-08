using System.Collections.Concurrent;
using SoundFlow.Security.Models;

namespace SoundFlow.Security.Stores;

/// <summary>
/// A reference implementation of <see cref="IFingerprintStore"/> that stores data in memory.
/// Suitable for small libraries, unit tests, or caching layers.
/// </summary>
public class InMemoryFingerprintStore : IFingerprintStore
{
    // The Inverted Index: Map Hash -> List of (TrackId, TimeOffset)
    private readonly ConcurrentDictionary<uint, List<FingerprintMatchCandidate>> _index = new();

    /// <inheritdoc />
    public Task InsertAsync(AudioFingerprint fingerprint)
    {
        foreach (var hashData in fingerprint.Hashes)
        {
            var candidate = new FingerprintMatchCandidate(fingerprint.TrackId, hashData.TimeOffset);
            
            _index.AddOrUpdate(
                hashData.Hash,
                _ => [candidate],
                (_, list) =>
                {
                    lock (list)
                    {
                        list.Add(candidate);
                    }
                    return list;
                });
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<List<FingerprintMatchCandidate>> QueryHashAsync(uint hash)
    {
        if (!_index.TryGetValue(hash, out var candidates))
            return Task.FromResult(new List<FingerprintMatchCandidate>());
        
        lock (candidates)
        {
            // Return a copy to ensure thread safety during iteration by the caller
            return Task.FromResult(new List<FingerprintMatchCandidate>(candidates));
        }
    }
}