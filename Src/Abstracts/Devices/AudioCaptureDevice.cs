using SoundFlow.Structs;

namespace SoundFlow.Abstracts.Devices;

/// <summary>
/// Represents a capture (input) audio device.
/// </summary>
public abstract class AudioCaptureDevice : AudioDevice
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCaptureDevice"/> class.
    /// </summary>
    /// <param name="engine">The parent audio engine.</param>
    /// <param name="format">The desired audio format.</param>
    /// <param name="config">The device configuration.</param>
    protected AudioCaptureDevice(AudioEngine engine, AudioFormat format, DeviceConfig config) : base(engine, format, config) { }
}