namespace SoundFlow.Backends.MiniAudio.Enums;

/// <summary>
/// Defines the stream types for OpenSL ES on Android, categorizing audio output for system management.
/// </summary>
public enum OpenSlStreamType
{
    /// <summary>
    /// Default stream type.
    /// </summary>
    Default = 0,
    /// <summary>
    /// For voice call audio.
    /// </summary>
    Voice,
    /// <summary>
    /// For system sounds.
    /// </summary>
    System,
    /// <summary>
    /// For ringtones.
    /// </summary>
    Ring,
    /// <summary>
    /// For media playback.
    /// </summary>
    Media,
    /// <summary>
    /// For alarms.
    /// </summary>
    Alarm,
    /// <summary>
    /// For notifications.
    /// </summary>
    Notification
}