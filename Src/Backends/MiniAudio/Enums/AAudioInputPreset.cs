namespace SoundFlow.Backends.MiniAudio.Enums;

/// <summary>
/// Defines input presets for AAudio recording on Android, optimizing capture for specific microphone configurations or scenarios.
/// </summary>
public enum AAudioInputPreset
{
    /// <summary>
    /// Default input preset.
    /// </summary>
    Default = 0,
    /// <summary>
    /// Generic input preset.
    /// </summary>
    Generic,
    /// <summary>
    /// For camcorder recording.
    /// </summary>
    Camcorder,
    /// <summary>
    /// For voice recognition.
    /// </summary>
    VoiceRecognition,
    /// <summary>
    /// For voice communication.
    /// </summary>
    VoiceCommunication,
    /// <summary>
    /// For unprocessed audio from the microphone.
    /// </summary>
    Unprocessed,
    /// <summary>
    /// For voice performance (e.g., singing).
    /// </summary>
    VoicePerformance
}