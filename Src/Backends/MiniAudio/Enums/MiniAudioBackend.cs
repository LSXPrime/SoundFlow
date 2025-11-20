namespace SoundFlow.Backends.MiniAudio.Enums;

/// <summary>
/// Represents the available low-level audio backends that MiniAudio can use.
/// </summary>
/// <remarks>
/// The integer values of these enum members directly correspond to the native `ma_backend` enum in MiniAudio.
/// </remarks>
public enum MiniAudioBackend
{
    /// <summary>
    /// The Windows Audio Session API, the modern standard for audio on Windows. (Windows Vista+)
    /// </summary>
    Wasapi = 1,

    /// <summary>
    /// The legacy DirectSound API. (Windows)
    /// </summary>
    DirectSound = 2,

    /// <summary>
    /// The legacy Windows MultiMedia API (WaveOut). (Windows)
    /// </summary>
    WinMm = 3,

    /// <summary>
    /// The standard audio API for macOS and iOS.
    /// </summary>
    CoreAudio = 4,

    /// <summary>
    /// A common audio server on OpenBSD.
    /// </summary>
    Sndio = 5,

    /// <summary>
    /// An audio API for BeOS.
    /// </summary>
    Audio4 = 6,

    /// <summary>
    /// The Open Sound System, common on BSD and older Linux systems.
    /// </summary>
    Oss = 7,

    /// <summary>
    /// A common sound server on modern Linux desktops.
    /// </summary>
    PulseAudio = 8,

    /// <summary>
    /// The Advanced Linux Sound Architecture, the standard low-level audio API on Linux.
    /// </summary>
    Alsa = 9,

    /// <summary>
    /// The JACK Audio Connection Kit, a professional low-latency audio server for Linux and macOS.
    /// </summary>
    Jack = 10,

    /// <summary>
    /// The modern low-latency audio API for Android. (Android 8.0+)
    /// </summary>
    AAudio = 11,

    /// <summary>
    /// The legacy audio API for Android.
    /// </summary>
    OpenSl = 12,

    /// <summary>
    /// The Web Audio API, for use in WebAssembly environments.
    /// </summary>
    WebAudio = 13,

    /// <summary>
    /// A placeholder for a custom user-defined backend. Not currently supported by this wrapper.
    /// </summary>
    Custom = 14,
    
    /// <summary>
    /// A silent backend that consumes and discards audio data. Used as a fallback.
    /// </summary>
    Null = 0
}