using SoundFlow.Enums;

namespace SoundFlow.Structs;

/// <summary>
/// Represents the format of an audio stream, including sample format, channel count, and sample rate.
/// This is a record struct, providing value-based equality and a non-nullable value type.
/// </summary>
public record struct AudioFormat
{
    /// <summary>
    /// Gets or sets the sample format (e.g., S16, F32).
    /// </summary>
    public SampleFormat Format;

    /// <summary>
    /// Gets or sets the number of audio channels (e.g., 1 for mono, 2 for stereo).
    /// </summary>
    public int Channels;

    /// <summary>
    /// Gets or sets the sample rate in Hertz (e.g., 44100, 48000).
    /// </summary>
    public int SampleRate;
    
    /// <summary>
    /// Gets the inverse of the sample rate.
    /// </summary>
    public float InverseSampleRate => 1f / SampleRate;

    #region Presets

    /// <summary>
    /// Preset for standard Compact Disc (CD) audio.
    /// </summary>
    /// <remarks>
    /// Format: S16, Channels: 2 (Stereo), Sample Rate: 44100 Hz.
    /// </remarks>
    public static readonly AudioFormat Cd = new()
    {
        Format = SampleFormat.S16,
        Channels = 2,
        SampleRate = 44100
    };

    /// <summary>
    /// Preset for standard DVD-Video audio.
    /// </summary>
    /// <remarks>
    /// Format: S16, Channels: 2 (Stereo), Sample Rate: 48000 Hz.
    /// </remarks>
    public static readonly AudioFormat Dvd = new()
    {
        Format = SampleFormat.S16,
        Channels = 2,
        SampleRate = 48000
    };
    
    /// <summary>
    /// Preset for standard DVD-Video audio using 32-bit floating-point samples.
    /// </summary>
    /// <remarks>
    /// Format: F32, Channels: 2 (Stereo), Sample Rate: 48000 Hz.
    /// </remarks>
    public static readonly AudioFormat DvdHq = new()
    {
        Format = SampleFormat.F32,
        Channels = 2,
        SampleRate = 48000
    };

    /// <summary>
    /// Preset for common studio recording (24-bit, 96 kHz).
    /// </summary>
    /// <remarks>
    /// Format: S24, Channels: 2 (Stereo), Sample Rate: 96000 Hz.
    /// </remarks>
    public static readonly AudioFormat Studio = new()
    {
        Format = SampleFormat.S24,
        Channels = 2,
        SampleRate = 96000
    };

    /// <summary>
    /// Preset for common studio recording using 32-bit floating-point samples.
    /// </summary>
    /// <remarks>
    /// Format: F32, Channels: 2 (Stereo), Sample Rate: 96000 Hz.
    /// </remarks>
    public static readonly AudioFormat StudioHq = new()
    {
        Format = SampleFormat.F32,
        Channels = 2,
        SampleRate = 96000
    };

    /// <summary>
    /// Preset for standard broadcast audio (mono).
    /// </summary>
    /// <remarks>
    /// Format: S16, Channels: 1 (Mono), Sample Rate: 48000 Hz. Often used for voice-over.
    /// </remarks>
    public static readonly AudioFormat Broadcast = new()
    {
        Format = SampleFormat.S16,
        Channels = 1,
        SampleRate = 48000
    };

    /// <summary>
    /// Preset for telephony and VoIP audio.
    /// </summary>
    /// <remarks>
    /// Format: U8, Channels: 1 (Mono), Sample Rate: 8000 Hz.
    /// </remarks>
    public static readonly AudioFormat Telephony = new()
    {
        Format = SampleFormat.U8,
        Channels = 1,
        SampleRate = 8000
    };

    #endregion
}