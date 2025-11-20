namespace SoundFlow.Metadata.Models;

/// <summary>
///     The mode of the audio bitrate (Constant, Variable, etc.).
/// </summary>
public enum BitrateMode
{
    /// <summary>
    /// Constant bitrate (CBR) mode. Indicates that the audio bitrate is constant.
    /// </summary>
    CBR,

    /// <summary>
    /// Variable bitrate (VBR) mode. Indicates that the audio bitrate varies.
    /// </summary>
    VBR,

    /// <summary>
    /// Average bitrate (ABR) mode. Indicates that the audio bitrate is averaged.
    /// </summary>
    ABR,

    /// <summary>
    /// Unknown bitrate mode. Indicates that the bitrate mode is unknown.
    /// </summary>
    Unknown
}

/// <summary>
///     Holds the format, tag, and cue information for an audio file.
/// </summary>
public class SoundFormatInfo
{
    /// <summary>
    ///     The common name of the audio format (e.g., "WAV", "MP3").
    /// </summary>
    public string FormatName { get; set; } = string.Empty;

    /// <summary>
    ///     The unique, lowercase string identifier for the audio format (e.g., "wav", "mp3", "flac").
    ///     This is used by the engine to resolve the correct codec factory.
    /// </summary>
    public string FormatIdentifier { get; set; } = string.Empty;

    /// <summary>
    ///     The name of the specific audio codec used (e.g., "PCM", "MPEG Layer III").
    /// </summary>
    public string CodecName { get; set; } = string.Empty;

    /// <summary>
    ///     The version of the container or format (e.g., "MPEG 1", "AIFF-C").
    /// </summary>
    public string ContainerVersion { get; set; } = string.Empty;

    /// <summary>
    ///     The duration of the audio.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    ///     The number of audio channels (1 for mono, 2 for stereo).
    /// </summary>
    public int ChannelCount { get; set; }

    /// <summary>
    ///     The sample rate, in Hertz (e.g., 44100).
    /// </summary>
    public int SampleRate { get; set; }

    /// <summary>
    ///     The number of bits per sample (e.g., 16, 24). Not applicable for lossy formats (0).
    /// </summary>
    public int BitsPerSample { get; set; }

    /// <summary>
    ///     The average bitrate in bits per second (bps).
    /// </summary>
    public int Bitrate { get; set; }

    /// <summary>
    ///     The bitrate mode (CBR, VBR, etc.).
    /// </summary>
    public BitrateMode BitrateMode { get; set; }

    /// <summary>
    ///     Indicates if the audio codec is lossless.
    /// </summary>
    public bool IsLossless { get; set; }

    /// <summary>
    ///     The metadata tags (artist, title, etc.) read from the file. Null if not read or not present.
    /// </summary>
    public SoundTags? Tags { get; set; }

    /// <summary>
    ///     The embedded cue sheet defining tracks within the file. Null if not read or not present.
    /// </summary>
    public CueSheet? Cues { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        return
            $"Format: {FormatName} ({CodecName}) / {(string.IsNullOrEmpty(ContainerVersion) ? "N/A" : ContainerVersion)}\n" +
            $"Duration: {Duration:hh\\:mm\\:ss\\.fff}\n" +
            $"Channels: {ChannelCount}\n" +
            $"Sample Rate: {SampleRate} Hz\n" +
            $"Bits Per Sample: {(BitsPerSample > 0 ? BitsPerSample.ToString() : "N/A")}\n" +
            $"Bitrate: {Bitrate / 1000} kbps ({BitrateMode}){(IsLossless ? " [Lossless]" : "")}\n" +
            (Tags != null ? $"\nTags:\n{Tags}" : "") +
            (Cues != null ? $"\nCue Sheet:\n{Cues}" : "");
    }
}