namespace SoundFlow.Security.Models;

/// <summary>
/// Represents the complete acoustic fingerprint of an audio source.
/// </summary>
public class AudioFingerprint
{
    /// <summary>
    /// Gets or sets the unique identifier for the audio source.
    /// </summary>
    public string TrackId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the list of generated hashes.
    /// </summary>
    public List<FingerprintHash> Hashes { get; set; } = [];

    /// <summary>
    /// Gets or sets the total duration of the analyzed audio in seconds.
    /// </summary>
    public double DurationSeconds { get; set; }
}