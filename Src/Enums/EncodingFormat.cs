namespace SoundFlow.Enums;

/// <summary>
///     Supported audio encoding formats.
/// </summary>
/// <remarks>
///     Current backend (miniaudio) supports only Wav for encoding.
/// </remarks>
public enum EncodingFormat
{
    /// <summary>
    /// Unknown encoding format.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Waveform Audio File Format.
    /// </summary>
    Wav,

    /// <summary>
    /// Free Lossless Audio Codec.
    /// </summary>
    Flac,

    /// <summary>
    /// MPEG-1 or MPEG-2 Audio Layer III.
    /// </summary>
    Mp3,

    /// <summary>
    /// Ogg Vorbis audio format.
    /// </summary>
    Vorbis
}
