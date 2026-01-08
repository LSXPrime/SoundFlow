using System.Diagnostics;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Security.Analyzers;
using SoundFlow.Security.Configuration;
using SoundFlow.Security.Models;
using SoundFlow.Security.Stores;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Security;

/// <summary>
/// Provides high-level functionality to identify audio content using fingerprints.
/// </summary>
public static class AudioIdentifier
{
    /// <summary>
    /// Generates a fingerprint for the provided audio data by processing the entire stream immediately.
    /// </summary>
    /// <param name="provider">The audio data provider.</param>
    /// <param name="config">Optional configuration.</param>
    /// <returns>A generated <see cref="AudioFingerprint"/>.</returns>
    public static AudioFingerprint GenerateFingerprint(ISoundDataProvider provider, FingerprintConfiguration? config = null)
    {
        config ??= new FingerprintConfiguration();
        
        var analyzer = new ContentFingerprintAnalyzer(
            new AudioFormat
            {
                SampleRate = provider.SampleRate,
                Channels = provider.FormatInfo?.ChannelCount ?? 2,
                Format = SampleFormat.F32,
                Layout = AudioFormat.GetLayoutFromChannels(provider.FormatInfo?.ChannelCount ?? 2)
            }, 
            config);

        // Process audio in blocks
        const int blockSize = 4096;
        var buffer = new float[blockSize];
        
        if (provider.CanSeek) provider.Seek(0);

        while (true)
        {
            var read = provider.ReadBytes(buffer);
            if (read == 0) break;
            
            analyzer.Process(buffer.AsSpan(0, read), provider.FormatInfo?.ChannelCount ?? 2);
        }

        var hashes = analyzer.GetGeneratedHashes();
        var duration = provider.Length / (double)(provider.SampleRate * (provider.FormatInfo?.ChannelCount ?? 2));

        return new AudioFingerprint
        {
            Hashes = hashes.ToList(),
            DurationSeconds = duration
        };
    }

    /// <summary>
    /// Attempts to identify the audio content provided by the data provider by matching it against the store.
    /// Uses a histogram of time-deltas to find the best alignment.
    /// </summary>
    /// <param name="provider">The query audio provider.</param>
    /// <param name="store">The fingerprint store to search.</param>
    /// <param name="config">Optional configuration.</param>
    /// <returns>A <see cref="FingerprintResult"/> detailing the match.</returns>
    public static async Task<Result<FingerprintResult>> IdentifyAsync(ISoundDataProvider provider, IFingerprintStore store, FingerprintConfiguration? config = null)
    {
        var sw = Stopwatch.StartNew();
        config ??= new FingerprintConfiguration();
        
        // 1. Generate Fingerprint for Query
        var queryFingerprint = GenerateFingerprint(provider, config);
        var queryTotalHashes = queryFingerprint.Hashes.Count;
        
        if (queryTotalHashes == 0)
        {
            return Result<FingerprintResult>.Fail(new NotFoundError("Fingerprint", "No hashes found in query audio."));
        }

        // 2. Query Store for Matches
        // Map: TrackId -> Dictionary<TimeDelta, Count>
        // Ideally, TimeDelta = DatabaseTime - QueryTime should be constant for a matching track.
        var timeDeltaHistograms = new Dictionary<string, Dictionary<int, int>>();

        foreach (var queryHash in queryFingerprint.Hashes)
        {
            var matches = await store.QueryHashAsync(queryHash.Hash);
            
            foreach (var match in matches)
            {
                if (!timeDeltaHistograms.TryGetValue(match.TrackId, out var histogram))
                {
                    histogram = new Dictionary<int, int>();
                    timeDeltaHistograms[match.TrackId] = histogram;
                }

                // Calculate relative time offset
                var delta = match.TrackTimeOffset - queryHash.TimeOffset;

                if (!histogram.TryAdd(delta, 1))
                {
                    histogram[delta]++;
                }
            }
        }

        // 3. Score Matches
        string? bestTrackId = null;
        var bestScore = 0;
        var bestDelta = 0;

        foreach (var (trackId, histogram) in timeDeltaHistograms)
        {
            foreach (var bin in histogram)
            {
                if (bin.Value <= bestScore) 
                    continue;
                
                bestScore = bin.Value;
                bestTrackId = trackId;
                bestDelta = bin.Key;
            }
        }

        sw.Stop();

        // 4. Validate against threshold

        // A. Absolute floor (MinConfidenceThreshold) to filter out very short/empty clips.
        if (bestTrackId == null || bestScore < config.MinConfidenceThreshold) return Result<FingerprintResult>.Fail(new NotFoundError("Fingerprint", $"No match found with a relative score above {config.MinConfidenceThreshold} (had {bestScore})."));
        var relativeScore = (double)bestScore / queryTotalHashes;

        // B. Relative floor (MinRelativeConfidence) to filter out random collisions in long/dense queries.
        if (relativeScore >= config.MinRelativeConfidence)
        {
            // Calculate time offset in seconds: Frames * HopSize / SampleRate
            var hopSize = config.FftSize / config.OverlapFactor;
            var timeOffsetSeconds = (double)bestDelta * hopSize / provider.SampleRate;

            Log.Debug($"Match: {bestTrackId} | Score: {bestScore} ({relativeScore:P2}) | Time: {timeOffsetSeconds:F2}s");

            return Result<FingerprintResult>.Ok(new FingerprintResult
            {
                TrackId = bestTrackId,
                Confidence = bestScore,
                MatchTimeSeconds = timeOffsetSeconds,
                ProcessingTime = sw.Elapsed
            });
        }

        Log.Info($"Rejected match '{bestTrackId}': Score {bestScore} is below relative threshold {config.MinRelativeConfidence:P0} (Actual: {relativeScore:P2}).");

        return Result<FingerprintResult>.Fail(new NotFoundError("Fingerprint", $"Relative score {relativeScore:P2} is below threshold {config.MinRelativeConfidence:P0}."));
    }
}