using SoundFlow.Enums;
using SoundFlow.Structs;

namespace SoundFlow.Abstracts.Devices;

/// <summary>
/// Represents an initialized audio device, managed by an <see cref="AudioEngine"/>.
/// </summary>
public abstract class AudioDevice : IDisposable
{
    /// <summary>
    /// Gets the parent engine that manages this device.
    /// </summary>
    public AudioEngine Engine { get; }

    /// <summary>
    /// Gets the informational struct for the physical device.
    /// </summary>
    public DeviceInfo? Info { get; internal init; }
    
    /// <summary>
    /// Gets the configuration struct for the physical device initialization process.
    /// </summary>
    public DeviceConfig Config { get; internal init; }

    /// <summary>
    /// Gets the capability of this device (e.g., Playback, Record).
    /// </summary>
    public Capability Capability { get; internal init; }
    
    /// <summary>
    /// Gets the audio format information (sample rate, channels, etc.) for this device.
    /// </summary>
    public AudioFormat Format { get; }

    /// <summary>
    /// Gets a value indicating whether the device is currently running.
    /// </summary>
    public bool IsRunning { get; protected set; }

    /// <summary>
    /// Gets a value indicating whether this device has been disposed.
    /// </summary>
    public bool IsDisposed { get; protected set; }
    
    /// <summary>
    /// Occurs when the audio device is disposed.
    /// </summary>
    public EventHandler? OnDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioDevice"/> class.
    /// </summary>
    /// <param name="engine">The parent audio engine.</param>
    /// <param name="format">The desired audio format.</param>
    /// <param name="config">The device configuration.</param>
    protected AudioDevice(AudioEngine engine, AudioFormat format, DeviceConfig config)
    {    
        Format = format;
        Engine = engine;
        Config = config;
    }

    /// <summary>
    /// Starts the audio device.
    /// </summary>
    public abstract void Start();

    /// <summary>
    /// Stops the audio device.
    /// </summary>
    public abstract void Stop();
    
    /// <summary>
    /// Releases all resources used by the audio device.
    /// </summary>
    public abstract void Dispose();
    
    /// <summary>
    /// Called when the audio device is disposed.
    /// </summary>
    protected virtual void OnDisposedHandler() => OnDisposed?.Invoke(this, EventArgs.Empty);
}
