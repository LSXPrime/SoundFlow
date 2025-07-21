using SoundFlow.Enums;

namespace SoundFlow.Interfaces;

/// <summary>
/// Defines the interface for a sound player component.
/// </summary>
public interface ISoundPlayer : IDisposable
{
    /// <summary>
    /// Gets the current playback state of the sound player.
    /// </summary>
    PlaybackState State { get; }

    /// <summary>
    /// Gets a value indicating whether the sound player is currently looping the audio.
    /// </summary>
    bool IsLooping { get; set; }

    /// <summary>
    /// Gets or sets the playback speed of the sound player.
    /// A value of 1.0 represents normal speed. Values greater than 1.0 increase the speed, and values less than 1.0 decrease it.
    /// </summary>
    /// <remarks>The current implementation uses linear interpolation which may affect the pitch.</remarks>
     float PlaybackSpeed { get; set; }
    
    /// <summary>
    /// Gets or sets the volume of the sound player
    /// </summary>
    float Volume { get; set; }

    /// <summary>
    /// Gets the current playback time in seconds, relative to the beginning of the audio.
    /// </summary>
    float Time { get; }

    /// <summary>
    /// Gets the total duration of the audio in seconds.
    /// </summary>
    float Duration { get; }
    
    /// <summary>
    /// Gets the loop start point in samples.
    /// </summary>
    int LoopStartSamples { get; }
    
    /// <summary>
    /// Gets the loop end point in samples. -1 indicates loop to the natural end of the audio.
    /// </summary>
    int LoopEndSamples { get; }
    
    /// <summary>
    /// Gets the loop start point in seconds.
    /// </summary>
    float LoopStartSeconds { get; }
    
    /// <summary>
    /// Gets the loop end point in seconds. -1 indicates loop to the natural end of the audio.
    /// </summary>
    float LoopEndSeconds { get; }

    /// <summary>
    /// Starts or resumes playback of the audio from the current position.
    /// If the player is already playing, calling this method may have no effect.
    /// If the player is stopped, playback starts from the beginning.
    /// If the player is paused, playback resumes from the paused position.
    /// </summary>
    void Play();

    /// <summary>
    /// Pauses playback of the audio at the current position.
    /// If the player is already paused or stopped, calling this method may have no effect.
    /// Playback can be resumed from the paused position by calling <see cref="Play"/>.
    /// </summary>
    void Pause();

    /// <summary>
    /// Stops playback of the audio and resets the playback position to the beginning.
    /// If the player is already stopped, calling this method has no effect.
    /// After stopping, playback can be restarted from the beginning by calling <see cref="Play"/>.
    /// </summary>
    void Stop();
    
    /// <summary>
    /// Seeks to a specific time in the audio playback using TimeSpan.
    /// </summary>
    /// <param name="time">The time to seek to as a TimeSpan, relative to the beginning of the audio.</param>
    /// <param name="seekOrigin">
    /// SeekOrigin - specifies how to calculate the offset
    /// <ul>
    /// <li><b>SeekOrigin.Begin</b>: Beginning of the audio stream</li>
    /// <li><b>SeekOrigin.Current</b>: Current position of the audio stream</li>
    /// <li><b>SeekOrigin.End:</b> Duration of the audio stream (offset has to be negative)</li>
    /// </ul>
    /// </param>
    bool Seek(TimeSpan time, SeekOrigin seekOrigin = SeekOrigin.Begin);

    /// <summary>
    /// Seeks to a specific time in the audio playback.
    /// </summary>
    /// <param name="time">The time in seconds to seek to, relative to the beginning of the audio.</param>
    bool Seek(float time);

    /// <summary>
    /// Seeks to a specific sample offset in the audio playback.
    /// </summary>
    /// <param name="sampleOffset">The sample offset to seek to, relative to the beginning of the audio data.</param>
    bool Seek(int sampleOffset);
    
    /// <summary>
    /// Sets the loop points for the sound player in seconds.
    /// </summary>
    /// <param name="startTime">The loop start time in seconds. Must be non-negative.</param>
    /// <param name="endTime">The loop end time in seconds, optional. Use -1 or null to loop to the natural end of the audio. Must be greater than or equal to startTime, or -1.</param>
    void SetLoopPoints(float startTime, float? endTime = -1f);

    /// <summary>
    /// Sets the loop points for the sound player in samples.
    /// </summary>
    /// <param name="startSample">The loop start sample. Must be non-negative.</param>
    /// <param name="endSample">The loop end sample, optional. Use -1 or null to loop to the natural end of the audio. Must be greater than or equal to startSample, or -1.</param>
    void SetLoopPoints(int startSample, int endSample = -1);
    
    
    /// <summary>
    /// Sets the loop points for the sound player using TimeSpan.
    /// </summary>
    /// <param name="startTime">The loop start time as a TimeSpan. Must be non-negative.</param>
    /// <param name="endTime">The loop end time as a TimeSpan, optional. Use null to loop to the natural end of the audio. Must be greater than or equal to startTime, or null.</param>
    void SetLoopPoints(TimeSpan startTime, TimeSpan? endTime = null);
}
