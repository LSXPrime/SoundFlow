using SoundFlow.Components;
using SoundFlow.Structs;
using SoundFlow.Structs.Events;

namespace SoundFlow.Abstracts.Devices;

/// <summary>
/// Represents a playback (output) audio device.
/// </summary>
public abstract class AudioPlaybackDevice : AudioDevice
{
    /// <summary>
    /// Gets the master mixer for this device. All audio to be played on this device
    /// must be routed to this mixer.
    /// </summary>
    public Mixer MasterMixer { get; }
    
    /// <summary>
    /// Cached event args object to prevent GC allocations every frame.
    /// </summary>
    internal readonly AudioFramesRenderedEventArgs CachedRenderEventArgs;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPlaybackDevice"/> class.
    /// </summary>
    /// <param name="engine">The parent audio engine.</param>
    /// <param name="format">The desired audio format.</param>
    /// <param name="config">The device configuration.</param>
    protected AudioPlaybackDevice(AudioEngine engine, AudioFormat format, DeviceConfig config) : base(engine, format, config)
    {
        MasterMixer = new Mixer(engine, Format, isMasterMixer: true) { ParentDevice = this };
        CachedRenderEventArgs = new AudioFramesRenderedEventArgs(this, 0);
    }
}