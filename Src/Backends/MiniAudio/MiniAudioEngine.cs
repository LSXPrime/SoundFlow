using SoundFlow.Abstracts;
using SoundFlow.Structs;
using SoundFlow.Utils;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Backends.MiniAudio.Structs;

namespace SoundFlow.Backends.MiniAudio;

/// <summary>
/// An audio engine based on the MiniAudio library.
/// </summary>
public class MiniAudioEngine : AudioEngine
{
    private nint _context;
    private readonly List<AudioDevice> _activeDevices = [];
    private readonly MiniAudioBackend[]? _backendPriority;

    internal static readonly Native.AudioCallback DataCallback = OnAudioData;

    private readonly ConcurrentDictionary<nint, MiniAudioDevice> _deviceMap = new();
    private static readonly ConcurrentDictionary<nint, GCHandle> ActiveEngineHandles = new();

    /// <summary>
    /// Gets a list of audio backends that are available on the current operating system.
    /// </summary>
    public static IReadOnlyList<MiniAudioBackend> AvailableBackends { get; }

    /// <summary>
    /// Gets the low-level audio backend that was successfully initialized and is currently active.
    /// </summary>
    /// <remarks>
    /// This property will be null if the user didn't pass the preferred backends array while creating the engine.
    /// In this case, MiniAudio will automatically select a backend and won't return it back.
    /// </remarks>
    public MiniAudioBackend ActiveBackend { get; private set; }

    static MiniAudioEngine()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            AvailableBackends =
            [
                MiniAudioBackend.Wasapi,
                MiniAudioBackend.DirectSound,
                MiniAudioBackend.WinMm
            ];
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || OperatingSystem.IsMacCatalyst())
        {
            AvailableBackends =
            [
                MiniAudioBackend.CoreAudio,
                MiniAudioBackend.Jack
            ];
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            AvailableBackends =
            [
                MiniAudioBackend.Alsa,
                MiniAudioBackend.PulseAudio,
                MiniAudioBackend.Jack,
                MiniAudioBackend.Oss
            ];
        }
        else if (OperatingSystem.IsAndroid())
        {
            AvailableBackends =
            [
                MiniAudioBackend.AAudio,
                MiniAudioBackend.OpenSl
            ];
        }
        else if (OperatingSystem.IsIOS())
        {
            AvailableBackends = [MiniAudioBackend.CoreAudio];
        }
        else if (OperatingSystem.IsFreeBSD())
        {
            AvailableBackends =
            [
                MiniAudioBackend.Oss,
                MiniAudioBackend.Sndio,
                MiniAudioBackend.PulseAudio
            ];
        }
        else
        {
            AvailableBackends = [];
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MiniAudioEngine"/> class.
    /// </summary>
    /// <param name="backendPriority">
    /// An optional, ordered list of backends to try for initialization. If provided, MiniAudio will
    /// attempt to use them in the specified order. If null or empty, MiniAudio's default probing
    /// mechanism will be used.
    /// </param>
    public MiniAudioEngine(IEnumerable<MiniAudioBackend>? backendPriority = null)
    {
        _backendPriority = backendPriority?.ToArray();
        InitializeBackend();
    }

    private static void OnAudioData(nint pDevice, nint pOutput, nint pInput, uint frameCount)
    {
        // Look up the GCHandle using the native device pointer.
        if (!ActiveEngineHandles.TryGetValue(pDevice, out var engineHandle) ||
            engineHandle.Target is not MiniAudioEngine engine ||
            !engine._deviceMap.TryGetValue(pDevice, out var managedDevice)) return;
        
        // Safely get the engine instance from the handle.
        managedDevice.Process(pOutput, pInput, frameCount);
    }

    internal static void RegisterEngineHandle(nint pDevice, MiniAudioEngine engine)
    {
        var engineHandle = GCHandle.Alloc(engine);
        ActiveEngineHandles.TryAdd(pDevice, engineHandle);
    }

    internal static void UnregisterEngineHandle(nint pDevice)
    {
        if (ActiveEngineHandles.TryRemove(pDevice, out var engineHandle))
        {
            engineHandle.Free();
        }
    }

    internal void RegisterDevice(nint pDevice, MiniAudioDevice device) => _deviceMap.TryAdd(pDevice, device);
    internal void UnregisterDevice(nint pDevice) => _deviceMap.TryRemove(pDevice, out _);

    /// <summary>
    /// Initializes the audio backend context.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    private void InitializeBackend()
    {
        _context = Native.AllocateContext();

        var pBackends = nint.Zero;
        uint backendCount = 0;

        // If a specific backend priority list is provided, marshal it for the native call.
        if (_backendPriority is { Length: > 0 })
        {
            // Convert the C# enum array to a native integer array.
            var nativeBackends = _backendPriority.Select(b => (int)b).ToArray();
            backendCount = (uint)nativeBackends.Length;

            // Allocate unmanaged memory and copy the array to it.
            pBackends = Marshal.AllocHGlobal(sizeof(int) * nativeBackends.Length);
            Marshal.Copy(nativeBackends, 0, pBackends, nativeBackends.Length);
        }

        try
        {
            // Use the marshaled pointer and count in the native call.
            var result = Native.ContextInit(pBackends, backendCount, nint.Zero, _context);
            if (result != MiniAudioResult.Success)
                throw new InvalidOperationException($"Unable to init MiniAudio context. Result: {result}");

            // Query and store the active backend for user feedback.
            ActiveBackend = Native.ContextGetBackend(_context);
        }
        finally
        {
            if (pBackends != nint.Zero) Marshal.FreeHGlobal(pBackends);
        }

        UpdateAudioDevicesInfo();

        // Register the built-in codec factory for formats supported by MiniAudio.
        RegisterCodecFactory(new MiniAudioCodecFactory());
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
    public override AudioPlaybackDevice InitializePlaybackDevice(DeviceInfo? deviceInfo, AudioFormat format,
        DeviceConfig? config = null)
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
    public override AudioCaptureDevice InitializeCaptureDevice(DeviceInfo? deviceInfo, AudioFormat format,
        DeviceConfig? config = null)
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
    public override FullDuplexDevice InitializeFullDuplexDevice(DeviceInfo? playbackDeviceInfo,
        DeviceInfo? captureDeviceInfo, AudioFormat format, DeviceConfig? config = null)
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

        UpdateAudioDevicesInfo();

        // WASAPI loopback is achieved by using the default playback device in capture mode.
        var defaultPlaybackDevice = PlaybackDevices.FirstOrDefault(d => d.IsDefault);

        if (defaultPlaybackDevice.Id == IntPtr.Zero)
            throw new NotSupportedException(
                "Could not find a default playback device to use for loopback recording. Ensure a default sound output device is set in your operating system.");

        config ??= GetDefaultDeviceConfig();

        ((MiniAudioDeviceConfig)config).Capture.IsLoopback = true;
        var device = InitializeCaptureDevice(defaultPlaybackDevice, format, config);
        return device;
    }

    /// <inheritdoc />
    public override AudioPlaybackDevice SwitchDevice(AudioPlaybackDevice oldDevice, DeviceInfo newDeviceInfo,
        DeviceConfig? config = null)
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
    public override AudioCaptureDevice SwitchDevice(AudioCaptureDevice oldDevice, DeviceInfo newDeviceInfo,
        DeviceConfig? config = null)
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
    public override FullDuplexDevice SwitchDevice(FullDuplexDevice oldDevice, DeviceInfo? newPlaybackInfo,
        DeviceInfo? newCaptureInfo, DeviceConfig? config = null)
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

    private static MiniAudioDeviceConfig GetDefaultDeviceConfig()
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
    public override void UpdateAudioDevicesInfo()
    {
        var result = Native.GetDevices(_context, out var pPlaybackDevices, out var pCaptureDevices,
            out var playbackCountUint, out var captureCountUint);

        if (result != MiniAudioResult.Success)
            throw new InvalidOperationException($"Unable to get devices. MiniAudio result: {result}");
        
        var playbackCount = (int)playbackCountUint;
        var captureCount = (int)captureCountUint;

        try
        {
            // Marshal playback devices
            if (playbackCount > 0 && pPlaybackDevices != nint.Zero)
            {
                // 1. Create array for native structs
                var nativePlayback = new DeviceInfoNative[playbackCount];
                // 2. Read from pointer into native structs
                pPlaybackDevices.ReadIntoArray(nativePlayback, playbackCount);
                
                // 3. Convert to public structs (Deep Copy)
                PlaybackDevices = new DeviceInfo[playbackCount];
                for (var i = 0; i < playbackCount; i++)
                {
                    PlaybackDevices[i] = ConvertFromNative(nativePlayback[i]);
                }
            }
            else
            {
                PlaybackDevices = [];
            }

            // Marshal capture devices
            if (captureCount > 0 && pCaptureDevices != nint.Zero)
            {
                var nativeCapture = new DeviceInfoNative[captureCount];
                pCaptureDevices.ReadIntoArray(nativeCapture, captureCount);

                CaptureDevices = new DeviceInfo[captureCount];
                for (var i = 0; i < captureCount; i++)
                {
                    CaptureDevices[i] = ConvertFromNative(nativeCapture[i]);
                }
            }
            else
            {
                CaptureDevices = [];
            }
        }
        finally
        {
            // 4. Now it is safe to free native memory, as we have copied the data to managed arrays
            if (pPlaybackDevices != nint.Zero) Native.FreeDeviceInfos(pPlaybackDevices, playbackCountUint);
            if (pCaptureDevices != nint.Zero) Native.FreeDeviceInfos(pCaptureDevices, captureCountUint);
        }
    }

    private static DeviceInfo ConvertFromNative(DeviceInfoNative native)
    {
        // Decode Name
        var name = string.Empty;
        if (native.NameBytes.Length > 0)
        {
            var count = Array.IndexOf(native.NameBytes, (byte)0);
            if (count == -1) count = native.NameBytes.Length;
            name = Encoding.UTF8.GetString(native.NameBytes, 0, count);
        }

        // Deep copy formats
        var formats = Array.Empty<NativeDataFormat>();
        if (native.NativeDataFormatCount > 0 && native.NativeDataFormats != IntPtr.Zero)
        {
            formats = native.NativeDataFormats.ReadArray<NativeDataFormat>((int)native.NativeDataFormatCount);
        }

        return new DeviceInfo
        {
            Id = native.Id,
            Name = name,
            IsDefault = native.IsDefault,
            SupportedDataFormats = formats
        };
    }
}