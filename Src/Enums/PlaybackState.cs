namespace SoundFlow.Enums;

/// <summary>
/// Describes the current state of a player or recorder.
/// </summary>
public enum PlaybackState
{
    /// <summary>
    /// The player or recorder is stopped.
    /// </summary>
    Stopped,

    /// <summary>
    /// The player or recorder is playing.
    /// </summary>
    Playing,

    /// <summary>
    /// The player or recorder is paused.
    /// </summary>
    Paused
}
