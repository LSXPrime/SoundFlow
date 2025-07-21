using SoundFlow.Components;
using SoundFlow.Structs;

namespace SoundFlow.Abstracts.Devices;

/// <summary>
/// A high-level abstraction that manages a paired playback and capture device for
/// easy full-duplex (simultaneous input and output) operation.
/// Ideal for live effects processing, VoIP, or instrument monitoring.
/// </summary>
public sealed class FullDuplexDevice : AudioDevice, IDisposable
{
    /// <summary>
    /// Gets the underlying playback device.
    /// </summary>
    public AudioPlaybackDevice PlaybackDevice { get; }

    /// <summary>
    /// Gets the underlying capture device.
    /// </summary>
    public AudioCaptureDevice CaptureDevice { get; }

    /// <summary>
    /// Gets the master mixer for the playback device. You can add other components
    /// (e.g., music players, synthesizers) to this mixer to play them.
    /// </summary>
    public Mixer MasterMixer => PlaybackDevice.MasterMixer;
    
    /// <summary>
    /// Occurs when audio data is processed by the capture device.
    /// </summary>
    public event AudioProcessCallback? OnAudioProcessed
    {
        add => CaptureDevice.OnAudioProcessed += value;
        remove => CaptureDevice.OnAudioProcessed -= value;
    }    

    /// <summary>
    /// Initializes a new instance of the <see cref="FullDuplexDevice"/> class.
    /// </summary>
    /// <param name="engine">The audio engine to use.</param>
    /// <param name="playbackDeviceInfo">The device information for the playback device.</param>
    /// <param name="captureDeviceInfo">The device information for the capture device.</param>
    /// <param name="format">The audio format to use.</param>
    /// <param name="config">The device configuration to use.</param>
    internal FullDuplexDevice(AudioEngine engine, DeviceInfo? playbackDeviceInfo, DeviceInfo? captureDeviceInfo, AudioFormat format, DeviceConfig config) : base(engine, format, config)
    {
        PlaybackDevice = engine.InitializePlaybackDevice(playbackDeviceInfo, format, config);
        CaptureDevice = engine.InitializeCaptureDevice(captureDeviceInfo, format, config);
    }
    
    /// <summary>
    /// Starts both the capture and playback devices.
    /// </summary>
    public override void Start()
    {
        if (IsRunning || IsDisposed) return;
        CaptureDevice.Start();
        PlaybackDevice.Start();
        IsRunning = true;
    }

    /// <summary>
    /// Stops both the capture and playback devices.
    /// </summary>
    public override void Stop()
    {
        if (!IsRunning || IsDisposed) return;
        PlaybackDevice.Stop();
        CaptureDevice.Stop();
        IsRunning = false;
    }

    /// <summary>
    /// Disposes of all resources, including the underlying capture and playback devices.
    /// </summary>
    public override void Dispose()
    {
        if (IsDisposed) return;
        
        Stop();
        
        // The components are disposed of by the mixer/device, but let's be explicit.
        PlaybackDevice.Dispose();
        CaptureDevice.Dispose();
        OnDisposedHandler();

        IsDisposed = true;
    }
}