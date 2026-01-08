namespace SoundFlow.Metadata.Models;

/// <summary>
/// Holds the metadata tags (artist, title, album art, etc.) for an audio file.
/// </summary>
public sealed class SoundTags
{
    /// <summary>
    /// Gets or sets the title of the audio file.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the artist of the audio file.
    /// </summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the album of the audio file.
    /// </summary>
    public string Album { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the genre of the audio file.
    /// </summary>
    public string Genre { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the year of the audio file, if available.
    /// </summary>
    public uint? Year { get; set; }

    /// <summary>
    /// Gets or sets the track number of the audio file, if available.
    /// </summary>
    public uint? TrackNumber { get; set; }

    /// <summary>
    /// Gets or sets the embedded album art of the audio file, if available.
    /// </summary>
    public byte[]? AlbumArt { get; set; }
    
    /// <summary>
    /// Gets the embedded, unsynchronized lyrics. Null if not present.
    /// </summary>
    public string? Lyrics { get; set; }

    
    /// <summary>
    /// Converts the SoundTags instance to a human-readable string.
    /// The string will contain the title, artist, album, genre, year, track number, album art size, and lyrics size.
    /// </summary>
    /// <returns>A human-readable string representation of the SoundTags instance.</returns>
    public override string ToString()
    {
        return $"  Title: {Title}\n" +
               $"  Artist: {Artist}\n" +
               $"  Album: {Album}\n" +
               $"  Genre: {Genre}\n" +
               $"  Year: {Year}\n" +
               $"  Track: {TrackNumber}\n" +
               $"  Album Art: {(AlbumArt != null ? $"{AlbumArt.Length} bytes" : "None")}" +
               (Lyrics != null ? $"\n  Lyrics: {(string.IsNullOrWhiteSpace(Lyrics) ? "Present (empty)" : $"{Lyrics.Length} characters")}" : "");
    }
}