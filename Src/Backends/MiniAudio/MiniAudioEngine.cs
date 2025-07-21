using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Structs;
using SoundFlow.Utils;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;

namespace SoundFlow.Backends.MiniAudio;

/// <summary>
/// An audio engine based on the MiniAudio library.
/// </summary>
public sealed class MiniAudioEngine : AudioEngine
{
    private nint _context;
    private readonly List<AudioDevice> _activeDevices = [];
    
    internal static readonly Native.AudioCallback DataCallback = OnAudioData;
    private static readonly ConcurrentDictionary<nint, MiniAudioDevice> DeviceMap = new();

    private static void OnAudioData(nint pDevice, nint pOutput, nint pInput, uint frameCount)
    {
        if (DeviceMap.TryGetValue(pDevice, out var managedDevice))
        {
            managedDevice.Process(pOutput, pInput, frameCount);
        }
    }

    internal void RegisterDevice(nint pDevice, MiniAudioDevice device) => DeviceMap.TryAdd(pDevice, device);
    internal void UnregisterDevice(nint pDevice) => DeviceMap.TryRemove(pDevice, out _);

    /// <inheritdoc />
    protected override void InitializeBackend()
    {
        _context = Native.AllocateContext();
        var result = Native.ContextInit(nint.Zero, 0, nint.Zero, _context);
        if (result != Result.Success)
            throw new InvalidOperationException("Unable to init context. " + result);
        
        UpdateDevicesInfo();
    }
    
    /// <inheritdoc />
    protected override void CleanupBackend()
    {
        foreach (var device in _activeDevices.ToList())
        {
            device.Dispose();
        }
        _activeDevices.Clear();
        
        Native.ContextUninit(_context);
        Native.Free(_context);
    }
    

    /// <inheritdoc />
    public override AudioPlaybackDevice InitializePlaybackDevice(DeviceInfo? deviceInfo, AudioFormat format, DeviceConfig? config = null)
    {
        if (config != null && config is not MiniAudioDeviceConfig)
            throw new ArgumentException($"config must be of type {typeof(MiniAudioDeviceConfig)}");
            
        config ??= GetDefaultDeviceConfig();
        var device = new MiniAudioPlaybackDevice(this, _context, deviceInfo, format, config);
        _activeDevices.Add(device);
        device.OnDisposed += OnDeviceDisposing;
        return device;
    }

    /// <inheritdoc />
    public override AudioCaptureDevice InitializeCaptureDevice(DeviceInfo? deviceInfo, AudioFormat format, DeviceConfig? config = null)
    {
        if (config != null && config is not MiniAudioDeviceConfig)
            throw new ArgumentException($"config must be of type {typeof(MiniAudioDeviceConfig)}");

        config ??= GetDefaultDeviceConfig();
        var device = new MiniAudioCaptureDevice(this, _context, deviceInfo, format, config);
        _activeDevices.Add(device);
        device.OnDisposed += OnDeviceDisposing;
        return device;
    }

    /// <inheritdoc />
    public override FullDuplexDevice InitializeFullDuplexDevice(DeviceInfo? playbackDeviceInfo, DeviceInfo? captureDeviceInfo, AudioFormat format, DeviceConfig? config = null)
    {
        if (config != null && config is not MiniAudioDeviceConfig)
            throw new ArgumentException($"config must be of type {typeof(MiniAudioDeviceConfig)}");
        
        config ??= GetDefaultDeviceConfig();
        var device = new FullDuplexDevice(this, playbackDeviceInfo, captureDeviceInfo, format, config);
        _activeDevices.Add(device);
        device.OnDisposed += OnDeviceDisposing;
        return device;
    }

    /// <inheritdoc />
    public override AudioCaptureDevice InitializeLoopbackDevice(AudioFormat format, DeviceConfig? config = null)
    {
        if (config != null && config is not MiniAudioDeviceConfig)
            throw new ArgumentException($"config must be of type {typeof(MiniAudioDeviceConfig)}");
        
        // Loopback devices are only supported on WASAPI
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new NotSupportedException("Loopback devices are only supported on Windows using WASAPI.");
        
        UpdateDevicesInfo();
        
        // WASAPI loopback is achieved by using the default playback device in capture mode.
        var defaultPlaybackDevice = PlaybackDevices.FirstOrDefault(d => d.IsDefault);
        
        if (defaultPlaybackDevice.Id == IntPtr.Zero)
            throw new NotSupportedException("Could not find a default playback device to use for loopback recording. Ensure a default sound output device is set in your operating system.");

        config ??= GetDefaultDeviceConfig();
        
        ((MiniAudioDeviceConfig)config).Capture.IsLoopback = true;
        var device = InitializeCaptureDevice(defaultPlaybackDevice, format, config);
        return device;
    }

    /// <inheritdoc />
    public override AudioPlaybackDevice SwitchDevice(AudioPlaybackDevice oldDevice, DeviceInfo newDeviceInfo, DeviceConfig? config = null)
    {
        var wasRunning = oldDevice.IsRunning;
        var preservedComponents = DeviceSwitcher.PreservePlaybackState(oldDevice);

        oldDevice.Dispose();

        var newDevice = InitializePlaybackDevice(newDeviceInfo, oldDevice.Format, config);
        DeviceSwitcher.RestorePlaybackState(newDevice, preservedComponents);

        if (wasRunning) newDevice.Start();

        return newDevice;
    }

    /// <inheritdoc />
    public override AudioCaptureDevice SwitchDevice(AudioCaptureDevice oldDevice, DeviceInfo newDeviceInfo, DeviceConfig? config = null)
    {
        var wasRunning = oldDevice.IsRunning;
        var preservedSubscribers = DeviceSwitcher.PreserveCaptureState(oldDevice);

        oldDevice.Dispose();

        var newDevice = InitializeCaptureDevice(newDeviceInfo, oldDevice.Format, config);
        DeviceSwitcher.RestoreCaptureState(newDevice, preservedSubscribers);

        if (wasRunning) newDevice.Start();

        return newDevice;
    }

    /// <inheritdoc />
    public override FullDuplexDevice SwitchDevice(FullDuplexDevice oldDevice, DeviceInfo? newPlaybackInfo, DeviceInfo? newCaptureInfo, DeviceConfig? config = null)
    {
        var wasRunning = oldDevice.IsRunning;
        
        // Preserve state from both underlying devices
        var preservedComponents = DeviceSwitcher.PreservePlaybackState(oldDevice.PlaybackDevice);
        var preservedSubscribers = DeviceSwitcher.PreserveCaptureState(oldDevice.CaptureDevice);

        // Use old device info if new info is not provided
        var playbackInfo = newPlaybackInfo ?? oldDevice.PlaybackDevice.Info;
        var captureInfo = newCaptureInfo ?? oldDevice.CaptureDevice.Info;
        
        oldDevice.Dispose();

        var newDevice = InitializeFullDuplexDevice(playbackInfo, captureInfo, oldDevice.Format, config);
        
        // Restore state to the new underlying devices
        DeviceSwitcher.RestorePlaybackState(newDevice.PlaybackDevice, preservedComponents);
        DeviceSwitcher.RestoreCaptureState(newDevice.CaptureDevice, preservedSubscribers);

        if (wasRunning) newDevice.Start();

        return newDevice;
    }

    private void OnDeviceDisposing(object? sender, EventArgs e)
    {
        if (sender is AudioDevice device)
        {
            _activeDevices.Remove(device);
        }
    }

    private MiniAudioDeviceConfig GetDefaultDeviceConfig()
    {
        return new MiniAudioDeviceConfig
        {
            PeriodSizeInFrames = 960,
            Playback = new DeviceSubConfig
            {
                ShareMode = ShareMode.Shared
            },
            Capture = new DeviceSubConfig
            {
                ShareMode = ShareMode.Shared
            }
        };
    }

    /// <inheritdoc />
    public override ISoundEncoder CreateEncoder(Stream stream, EncodingFormat encodingFormat, AudioFormat format)
    {
        return new MiniAudioEncoder(stream, encodingFormat, format.Format, format.Channels, format.SampleRate);
    }

    /// <inheritdoc />
    public override ISoundDecoder CreateDecoder(Stream stream, AudioFormat format)
    {
        return new MiniAudioDecoder(stream, format.Format, format.Channels, format.SampleRate);
    }
    
    /// <inheritdoc />
    public override void UpdateDevicesInfo()
    {
        var result = Native.GetDevices(_context, out var pPlaybackDevices, out var pCaptureDevices,
            out var playbackDeviceCountNint, out var captureDeviceCountNint);

        if (result != Result.Success)
            throw new InvalidOperationException($"Unable to get devices. MiniAudio result: {result}");

        var playbackCount = (uint)playbackDeviceCountNint;
        var captureCount = (uint)captureDeviceCountNint;

        try
        {
            // Marshal playback devices
            if (playbackCount > 0 && pPlaybackDevices != nint.Zero)
                PlaybackDevices = pPlaybackDevices.ReadArray<DeviceInfo>((int)playbackCount);
            else
                PlaybackDevices = [];

            // Marshal capture devices
            if (captureCount > 0 && pCaptureDevices != nint.Zero)
                CaptureDevices = pCaptureDevices.ReadArray<DeviceInfo>((int)captureCount);
            else
                CaptureDevices = [];
        }
        finally
        {
            if (pPlaybackDevices != nint.Zero) Native.FreeDeviceInfos(pPlaybackDevices, playbackCount);
            if (pCaptureDevices != nint.Zero) Native.FreeDeviceInfos(pCaptureDevices, captureCount);
        }
    }
}