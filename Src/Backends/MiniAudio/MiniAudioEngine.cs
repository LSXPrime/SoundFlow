#define BROWSER

using System.Runtime.InteropServices;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Backends.MiniAudio;

/// <summary>
///     An audio engine based on the MiniAudio library.
/// </summary>
public sealed class MiniAudioEngine(
    int sampleRate,
    Capability capability,
    SampleFormat sampleFormat = SampleFormat.F32,
    int channels = 2)
    : AudioEngine(sampleRate, capability, sampleFormat, channels)
{
    private Native.AudioCallback? _audioCallback;
    private nint _context;
    private nint _device = nint.Zero;
    private nint _currentPlaybackDeviceId = nint.Zero;
    private nint _currentCaptureDeviceId = nint.Zero;

    /// <inheritdoc />
    protected override bool RequiresBackendThread { get; } = false;


    /// <inheritdoc />
    protected override void InitializeAudioDevice()
    {
        Console.WriteLine("Allocating context.");
        _context = Native.AllocateContext();
        
        Console.WriteLine("Initializing context.");
        var result = (Result)Native.ContextInit(nint.Zero, 0, nint.Zero, _context);
        if (result != Result.Success)
        {
            Console.WriteLine("Unable to init context. " + result);
            throw new InvalidOperationException("Unable to init context. " + result);
        }
        
        Console.WriteLine("Context initialized successfully.");

        InitializeDeviceInternal(nint.Zero, nint.Zero);
    }


    private void InitializeDeviceInternal(nint playbackDeviceId, nint captureDeviceId)
    {
        if (_device != nint.Zero)
            CleanupCurrentDevice();

        _audioCallback ??= AudioCallback;
        
        Console.WriteLine("Allocating device config.");
        
        // Get Delegate Function Pointer
        var functionPointer = _audioCallback.Method.MethodHandle.GetFunctionPointer();
        
        var deviceConfig = Native.AllocateDeviceConfig((int)Capability, (int)SampleFormat, Channels, SampleRate,
            Marshal.GetFunctionPointerForDelegate(_audioCallback),
            playbackDeviceId,
            captureDeviceId);

        Console.WriteLine("Device config allocated successfully, Allocating device.");
        
        _device = Native.AllocateDevice();
        
        Console.WriteLine("Device allocated successfully, initializing device.");
        
        var result = (Result)Native.DeviceInit(_context, deviceConfig, _device);
        Native.Free(deviceConfig);

        if (result != Result.Success)
        {
            Console.WriteLine("Unable to init device. " + result);
            Native.Free(_device);
            _device = nint.Zero;
            throw new InvalidOperationException($"Unable to init device. {result}");
        }
        
        Console.WriteLine("Device initialized successfully, starting device.");

        result = (Result)Native.DeviceStart(_device);
        if (result != Result.Success)
        {
            Console.WriteLine("Unable to start device. " + result);
            CleanupCurrentDevice();
            throw new InvalidOperationException($"Unable to start device. {result}");
        }
        
        Console.WriteLine("Device started successfully.");

        UpdateDevicesInfo();
        CurrentPlaybackDevice = PlaybackDevices.FirstOrDefault(x => x.Id == playbackDeviceId);
        CurrentCaptureDevice = CaptureDevices.FirstOrDefault(x => x.Id == captureDeviceId);
        CurrentPlaybackDevice ??= PlaybackDevices.FirstOrDefault(x => x.IsDefault);
        CurrentCaptureDevice ??= CaptureDevices.FirstOrDefault(x => x.IsDefault);

        if (CurrentPlaybackDevice != null) _currentPlaybackDeviceId = CurrentPlaybackDevice.Value.Id;
        if (CurrentCaptureDevice != null) _currentCaptureDeviceId = CurrentCaptureDevice.Value.Id;
    }

    private void CleanupCurrentDevice()
    {
        if (_device == nint.Zero) return;
        _ = Native.DeviceStop(_device);
        Native.DeviceUninit(_device);
        Native.Free(_device);
        _device = nint.Zero;
    }


    private void AudioCallback(nint _, nint output, nint input, uint length)
    {
        var sampleCount = (int)length * Channels;
        if (Capability != Capability.Record) ProcessGraph(output, sampleCount);
        if (Capability != Capability.Playback) ProcessAudioInput(input, sampleCount);
    }


    /// <inheritdoc />
    protected override void ProcessAudioData() { }

    /// <inheritdoc />
    protected override void CleanupAudioDevice()
    {
        CleanupCurrentDevice();
        Native.ContextUninit(_context);
        Native.Free(_context);
    }


    /// <inheritdoc />
    protected internal override ISoundEncoder CreateEncoder(string filePath, EncodingFormat encodingFormat,
        SampleFormat sampleFormat, int encodingChannels, int sampleRate)
    {
        return new MiniAudioEncoder(filePath, encodingFormat, sampleFormat, encodingChannels, sampleRate);
    }

    /// <inheritdoc />
    protected internal override ISoundDecoder CreateDecoder(Stream stream)
    {
        return new MiniAudioDecoder(stream);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        CleanupAudioDevice();
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override void SwitchDevice(DeviceInfo deviceInfo, DeviceType type = DeviceType.Playback)
    {
        if (deviceInfo.Id == nint.Zero)
            throw new InvalidOperationException("Unable to switch device. Device ID is invalid.");

        switch (type)
        {
            case DeviceType.Playback:
                InitializeDeviceInternal(deviceInfo.Id, _currentCaptureDeviceId);
                break;
            case DeviceType.Capture:
                InitializeDeviceInternal(_currentPlaybackDeviceId, deviceInfo.Id);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid DeviceType for SwitchDevice.");
        }
    }
    
    /// <inheritdoc />
    public override void SwitchDevices(DeviceInfo? playbackDeviceInfo, DeviceInfo? captureDeviceInfo)
    {
        var playbackDeviceId = _currentPlaybackDeviceId;
        var captureDeviceId = _currentCaptureDeviceId;

        if (playbackDeviceInfo != null)
        {
            if (playbackDeviceInfo.Value.Id == nint.Zero)
                throw new InvalidOperationException("Invalid Playback Device ID provided for SwitchDevices.");
            playbackDeviceId = playbackDeviceInfo.Value.Id;
        }

        if (captureDeviceInfo != null)
        {
            if (captureDeviceInfo.Value.Id == nint.Zero)
                throw new InvalidOperationException("Invalid Capture Device ID provided for SwitchDevices.");
            captureDeviceId = captureDeviceInfo.Value.Id;
        }

        InitializeDeviceInternal(playbackDeviceId, captureDeviceId);
    }


    /// <inheritdoc />
    public override void UpdateDevicesInfo()
    {
        if (OperatingSystem.IsBrowser())
            return;

        #if !BROWSER
        var result = (Result)Native.GetDevices(_context, out var pPlaybackDevices, out var pCaptureDevices,
            out var playbackDeviceCount, out var captureDeviceCount);
        if (result != Result.Success)
            throw new InvalidOperationException("Unable to get devices.");

        PlaybackDeviceCount = (int)playbackDeviceCount;
        CaptureDeviceCount = (int)captureDeviceCount;

        if (pPlaybackDevices == nint.Zero && pCaptureDevices == nint.Zero)
        {
            PlaybackDevices = [];
            CaptureDevices = [];
            return;
        }

        PlaybackDevices = pPlaybackDevices.ReadArray<DeviceInfo>(PlaybackDeviceCount);
        CaptureDevices = pCaptureDevices.ReadArray<DeviceInfo>(CaptureDeviceCount);

        Native.Free(pPlaybackDevices);
        Native.Free(pCaptureDevices);

        if (playbackDeviceCount == 0) PlaybackDevices = [];
        if (captureDeviceCount == 0) CaptureDevices = [];
        #endif
    }
}