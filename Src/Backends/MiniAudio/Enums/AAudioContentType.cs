namespace SoundFlow.Backends.MiniAudio.Enums;

/// <summary>
/// Defines the content type of an AAudio stream on Android, informing the system about the audio's nature.
/// </summary>
public enum AAudioContentType
{
    /// <summary>
    /// Default content type.
    /// </summary>
    Default = 0,
    /// <summary>
    /// For spoken audio.
    /// </summary>
    Speech,
    /// <summary>
    /// For music.
    /// </summary>
    Music,
    /// <summary>
    /// For movie audio.
    /// </summary>
    Movie,
    /// <summary>
    /// For sonification (making data audible).
    /// </summary>
    Sonification
}