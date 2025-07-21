using SoundFlow.Abstracts.Devices;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Structs;

namespace SoundFlow.Abstracts;

/// <summary>
/// This event is raised when samples are processed by Input or Output components.
/// </summary>
public delegate void AudioProcessCallback(Span<float> samples, Capability capability);

/// <summary>
/// The base class for audio engines. An engine acts as a context manager for audio devices
/// and provides facilities for decoding and encoding audio.
/// </summary>
public abstract class AudioEngine : IDisposable
{
    private SoundComponent? _soloedComponent;
    private readonly object _lock = new();
    


    /// <summary>
    /// Initializes a new instance of the <see cref="AudioEngine"/> class.
    /// </summary>
    protected AudioEngine()
    {
        InitializeBackend();
    }
    
    /// <summary>
    /// Gets an array of available playback devices.
    /// </summary>
    public DeviceInfo[] PlaybackDevices { get; protected set; } = [];

    /// <summary>
    /// Gets an array of available capture devices.
    /// </summary>
    public DeviceInfo[] CaptureDevices { get; protected set; } = [];

    /// <summary>
    /// Initializes the audio backend context.
    /// </summary>
    protected abstract void InitializeBackend();

    /// <summary>
    /// Cleans up the audio backend context.
    /// </summary>
    protected abstract void CleanupBackend();
    
    /// <summary>
    /// Solos the specified sound component, muting all other components within this engine's devices.
    /// </summary>
    /// <param name="component">The component to solo.</param>
    public void SoloComponent(SoundComponent component)
    {
        lock (_lock)
        {
            _soloedComponent = component;
        }
    }

    /// <summary>
    /// Unsolos the specified sound component.
    /// </summary>
    /// <param name="component">The component to unsolo.</param>
    public void UnsoloComponent(SoundComponent component)
    {
        lock (_lock)
        {
            if (_soloedComponent == component)
            {
                _soloedComponent = null;
            }
        }
    }
    
    /// <summary>
    /// Gets the currently soloed component, if any.
    /// </summary>
    /// <returns>The soloed SoundComponent or null.</returns>
    public SoundComponent? GetSoloedComponent()
    {
        lock (_lock)
        {
            return _soloedComponent;
        }
    }

    /// <summary>
    /// Constructs a sound encoder specific to the implementation.
    /// </summary>
    /// <param name="stream">The stream to write encoded audio to.</param>
    /// <param name="encodingFormat">The desired audio encoding format.</param>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    /// <returns>An instance of a sound encoder.</returns>
    public abstract ISoundEncoder CreateEncoder(Stream stream, EncodingFormat encodingFormat, AudioFormat format);
    
    /// <summary>
    /// Constructs a sound decoder specific to the implementation.
    /// </summary>
    /// <param name="stream">The stream containing the audio data.</param>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    /// <returns>An instance of a sound decoder.</returns>
    public abstract ISoundDecoder CreateDecoder(Stream stream, AudioFormat format);
    
    /// <summary>
    /// Initializes and returns a playback device.
    /// </summary>
    /// <param name="deviceInfo">The device to initialize. Must be a playback-capable device.</param>
    /// <param name="format">The desired audio format.</param>
    /// <param name="config">Optional detailed configuration for the device and its backend.</param>
    /// <returns>An initialized <see cref="AudioPlaybackDevice"/>.</returns>
    public abstract AudioPlaybackDevice InitializePlaybackDevice(DeviceInfo? deviceInfo, AudioFormat format, DeviceConfig? config = null);

    /// <summary>
    /// Initializes and returns a capture device.
    /// </summary>
    /// <param name="deviceInfo">The device to initialize. Must be a capture-capable device.</param>
    /// <param name="format">The desired audio format.</param>
    /// <param name="config">Optional detailed configuration for the device and its backend.</param>
    /// <returns>An initialized <see cref="AudioCaptureDevice"/>.</returns>
    public abstract AudioCaptureDevice InitializeCaptureDevice(DeviceInfo? deviceInfo, AudioFormat format, DeviceConfig? config = null);
    
    /// <summary>
    /// Initializes a high-level full-duplex device for simultaneous input and output.
    /// This simplifies live effects processing by managing a paired capture and playback device.
    /// </summary>
    /// <param name="playbackDeviceInfo">The playback device to use. Use null for the system default.</param>
    /// <param name="captureDeviceInfo">The capture device to use. Use null for the system default.</param>
    /// <param name="format">The audio format to use for both devices.</param>
    /// <param name="config">Optional detailed configuration for the devices.</param>
    /// <returns>An initialized <see cref="FullDuplexDevice"/> ready for use.</returns>
    public abstract FullDuplexDevice InitializeFullDuplexDevice(DeviceInfo? playbackDeviceInfo, DeviceInfo? captureDeviceInfo, AudioFormat format, DeviceConfig? config = null);

    /// <summary>
    /// Initializes a loopback capture device, allowing for the recording of system audio output.
    /// </summary>
    /// <param name="format">The desired audio format for the loopback capture.</param>
    /// <param name="config">Optional detailed configuration for the device.</param>
    /// <returns>An initialized <see cref="AudioCaptureDevice"/> configured for loopback recording.</returns>
    /// <exception cref="NotSupportedException">Thrown if a default playback device (required for loopback) cannot be found.</exception>
    public abstract AudioCaptureDevice InitializeLoopbackDevice(AudioFormat format, DeviceConfig? config = null);
    
    /// <summary>
    /// Switches an active playback device to a new physical device, preserving its audio graph.
    /// The old device instance will be disposed.
    /// </summary>
    /// <param name="oldDevice">The playback device instance to replace.</param>
    /// <param name="newDeviceInfo">The info for the new physical device to use.</param>
    /// <param name="config">Optional configuration for the new device.</param>
    /// <returns>A new, active <see cref="AudioPlaybackDevice"/> instance.</returns>
    public abstract AudioPlaybackDevice SwitchDevice(AudioPlaybackDevice oldDevice, DeviceInfo newDeviceInfo, DeviceConfig? config = null);

    /// <summary>
    /// Switches an active capture device to a new physical device, preserving its event subscribers.
    /// The old device instance will be disposed.
    /// </summary>
    /// <param name="oldDevice">The capture device instance to replace.</param>
    /// <param name="newDeviceInfo">The info for the new physical device to use.</param>
    /// <param name="config">Optional configuration for the new device.</param>
    /// <returns>A new, active <see cref="AudioCaptureDevice"/> instance.</returns>
    public abstract AudioCaptureDevice SwitchDevice(AudioCaptureDevice oldDevice, DeviceInfo newDeviceInfo, DeviceConfig? config = null);

    /// <summary>
    /// Switches the devices used by a full-duplex instance, preserving its state.
    /// The old duplex device instance will be disposed.
    /// </summary>
    /// <param name="oldDevice">The full-duplex device instance to replace.</param>
    /// <param name="newPlaybackInfo">Info for the new playback device. If null, the existing playback device is used.</param>
    /// <param name="newCaptureInfo">Info for the new capture device. If null, the existing capture device is used.</param>
    /// <param name="config">Optional configuration for the new device(s).</param>
    /// <returns>A new, active <see cref="FullDuplexDevice"/> instance.</returns>
    public abstract FullDuplexDevice SwitchDevice(FullDuplexDevice oldDevice, DeviceInfo? newPlaybackInfo, DeviceInfo? newCaptureInfo, DeviceConfig? config = null);
    
    /// <summary>
    /// Retrieves the list of available playback and capture devices from the underlying audio backend.
    /// </summary>
    public abstract void UpdateDevicesInfo();

    #region IDisposable Support

    /// <summary>
    /// Gets a value indicating whether the audio engine has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }
    
    /// <summary>
    /// Cleans up resources before the object is garbage collected.
    /// </summary>
    ~AudioEngine()
    {
        Dispose(false);
    }

    /// <summary>
    /// Disposes of managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        if (disposing)
        {
            CleanupBackend();
        }

        IsDisposed = true;
    }

    /// <summary>
    /// Disposes of the audio engine and all associated devices.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}