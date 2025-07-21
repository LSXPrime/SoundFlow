using SoundFlow.Structs;

namespace SoundFlow.Abstracts.Devices;

/// <summary>
/// Represents a capture (input) audio device.
/// </summary>
public abstract class AudioCaptureDevice : AudioDevice
{
    /// <summary>
    /// Occurs when new audio samples are captured from this device.
    /// </summary>
    public event AudioProcessCallback? OnAudioProcessed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCaptureDevice"/> class.
    /// </summary>
    /// <param name="engine">The parent audio engine.</param>
    /// <param name="format">The desired audio format.</param>
    /// <param name="config">The device configuration.</param>
    protected AudioCaptureDevice(AudioEngine engine, AudioFormat format, DeviceConfig config) : base(engine, format, config) { }

    /// <summary>
    /// Invokes the <see cref="OnAudioProcessed"/> event with the captured samples.
    /// This method is intended to be called by the backend implementation.
    /// </summary>
    /// <param name="samples">The captured audio samples.</param>
    protected virtual void InvokeOnAudioProcessed(Span<float> samples)
    {
        OnAudioProcessed?.Invoke(samples, Capability);
    }
    
    /// <summary>
    /// Gets the invocation list of the OnAudioProcessed event. For internal engine use only.
    /// </summary>
    /// <returns>An array of delegates subscribed to the event.</returns>
    internal Delegate[] GetEventSubscribers()
    {
        return OnAudioProcessed?.GetInvocationList() ?? [];
    }
}
