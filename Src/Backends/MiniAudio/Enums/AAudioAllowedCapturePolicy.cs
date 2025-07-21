namespace SoundFlow.Backends.MiniAudio.Enums;

/// <summary>
/// Defines the allowed capture policy for an AAudio output stream on Android,
/// controlling whether other applications can capture its audio.
/// </summary>
public enum AAudioAllowedCapturePolicy
{
    /// <summary>
    /// Default capture policy.
    /// </summary>
    Default = 0,
    /// <summary>
    /// Allows all applications to capture this stream.
    /// </summary>
    All,
    /// <summary>
    /// Only allows system applications to capture this stream.
    /// </summary>
    System,
    /// <summary>
    /// Prevents other applications from capturing this stream.
    /// </summary>
    None
}