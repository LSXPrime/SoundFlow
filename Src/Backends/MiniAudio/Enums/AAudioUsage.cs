namespace SoundFlow.Backends.MiniAudio.Enums;

/// <summary>
/// Defines usage scenarios for AAudio streams on Android, guiding the system's resource management and routing.
/// </summary>
public enum AAudioUsage
{
    /// <summary>
    /// Default usage scenario.
    /// </summary>
    Default = 0,
    /// <summary>
    /// For media playback (music, video).
    /// </summary>
    Media,
    /// <summary>
    /// For voice communication (e.g., phone calls).
    /// </summary>
    VoiceCommunication,
    /// <summary>
    /// For communication signaling (e.g., ringtones for communication apps).
    /// </summary>
    VoiceCommunicationSignalling,
    /// <summary>
    /// For alarms.
    /// </summary>
    Alarm,
    /// <summary>
    /// For notifications.
    /// </summary>
    Notification,
    /// <summary>
    /// For notification ringtones.
    /// </summary>
    NotificationRingtone,
    /// <summary>
    /// For notification events.
    /// </summary>
    NotificationEvent,
    /// <summary>
    /// For accessibility features.
    /// </summary>
    AssistanceAccessibility,
    /// <summary>
    /// For navigation guidance.
    /// </summary>
    AssistanceNavigationGuidance,
    /// <summary>
    /// For sonification (making data audible).
    /// </summary>
    AssistanceSonification,
    /// <summary>
    /// For games.
    /// </summary>
    Game,
    /// <summary>
    /// For assistant applications.
    /// </summary>
    Assistant,
    /// <summary>
    /// For emergency sounds.
    /// </summary>
    Emergency,
    /// <summary>
    /// For safety-critical sounds.
    /// </summary>
    Safety,
    /// <summary>
    /// For vehicle status announcements.
    /// </summary>
    VehicleStatus,
    /// <summary>
    /// For general announcements.
    /// </summary>
    Announcement,
}