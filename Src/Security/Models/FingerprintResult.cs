namespace SoundFlow.Security.Models;

/// <summary>
/// Represents the final result of an audio identification attempt.
/// </summary>
public class FingerprintResult
{
    /// <summary>
    /// Gets the unique identifier of the matched track.
    /// </summary>
    public string TrackId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the confidence score of the match.
    /// This represents the number of hashes that temporally aligned with the database.
    /// </summary>
    public int Confidence { get; init; }

    /// <summary>
    /// Gets the calculated time offset (in seconds) where the query audio starts within the matched track.
    /// </summary>
    public double MatchTimeSeconds { get; init; }

    /// <summary>
    /// Gets the total processing time for the identification.
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }
}