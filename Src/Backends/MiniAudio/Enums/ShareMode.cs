namespace SoundFlow.Backends.MiniAudio.Enums;

/// <summary>
/// Defines how an audio device is to be opened by the backend, influencing sharing behavior and latency.
/// </summary>
public enum ShareMode
{
    /// <summary>
    /// The device can be shared by multiple audio applications simultaneously. This is generally the default.
    /// </summary>
    Shared,
    /// <summary>
    /// The device is opened for exclusive use by this application, providing the lowest possible latency
    /// but preventing other applications from using the device. Not all devices support exclusive mode.
    /// </summary>
    Exclusive
}