namespace SoundFlow.Backends.MiniAudio.Enums;

/// <summary>
/// Defines recording presets for OpenSL ES on Android, optimizing audio capture for specific scenarios.
/// </summary>
public enum OpenSlRecordingPreset
{
    /// <summary>
    /// Default recording preset.
    /// </summary>
    Default = 0,
    /// <summary>
    /// Generic recording preset.
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
    /// For unprocessed voice, typically for further processing.
    /// </summary>
    VoiceUnprocessed
}