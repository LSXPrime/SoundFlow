namespace SoundFlow.Enums;

/// <summary>
/// Types of filters supported by the Parametric Equalizer.
/// </summary>
public enum FilterType
{
    /// <summary>
    /// A peaking equalizer boosts or cuts a specific frequency range.
    /// </summary>
    Peaking,

    /// <summary>
    /// A low-shelf equalizer boosts or cuts all frequencies below a specific frequency.
    /// </summary>
    LowShelf,

    /// <summary>
    /// A high-shelf equalizer boosts or cuts all frequencies above a specific frequency.
    /// </summary>
    HighShelf,

    /// <summary>
    /// A low-pass filter removes high frequencies from the audio signal.
    /// </summary>
    LowPass,

    /// <summary>
    /// A high-pass filter removes low frequencies from the audio signal.
    /// </summary>
    HighPass,

    /// <summary>
    /// A band-pass filter removes all frequencies outside a specific frequency range.
    /// </summary>
    BandPass,

    /// <summary>
    /// A notch filter removes a specific frequency range from the audio signal.
    /// </summary>
    Notch,

    /// <summary>
    /// An all-pass filter changes the phase of the audio signal without affecting its frequency response.
    /// </summary>
    AllPass
}