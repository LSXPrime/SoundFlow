namespace SoundFlow.Enums;

/// <summary>
///     Describes the capabilities of a sound device.
/// </summary>
[Flags]
public enum Capability
{
    /// <summary>
    ///     The device is used for audio playback.
    /// </summary>
    Playback = 1,

    /// <summary>
    ///     The device is used for audio capture.
    /// </summary>
    Record = 2,

    /// <summary>
    ///     The device is used for both playback and capture.
    /// </summary>
    Mixed = Playback | Record,

    /// <summary>
    ///     The device is used for loopback recording (capturing the output).
    /// </summary>
    Loopback = 4
}
