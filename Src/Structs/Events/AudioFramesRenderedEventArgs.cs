using SoundFlow.Abstracts.Devices;

namespace SoundFlow.Structs.Events;

/// <summary>
/// Provides data for the AudioFramesRendered event.
/// </summary>
public class AudioFramesRenderedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the audio device that rendered the frames.
    /// </summary>
    public AudioDevice Device { get; internal set; }

    /// <summary>
    /// Gets the number of audio frames that were rendered.
    /// </summary>
    public int FrameCount { get; internal set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioFramesRenderedEventArgs"/> class.
    /// </summary>
    public AudioFramesRenderedEventArgs(AudioDevice device, int frameCount)
    {
        Device = device;
        FrameCount = frameCount;
    }
}