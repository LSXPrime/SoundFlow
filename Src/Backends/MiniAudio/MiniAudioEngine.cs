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
        _context = Native.AllocateContext();
        var result = Native.ContextInit(nint.Zero, 0, nint.Zero, _context);
        if (result != Result.Success)
            throw new InvalidOperationException("Unable to init context. " + result);

        InitializeDeviceInternal(nint.Zero, nint.Zero);
    }


    private void InitializeDeviceInternal(nint playbackDeviceId, nint captureDeviceId)
    {
        if (_device != nint.Zero)
            CleanupCurrentDevice();

        var deviceConfig = Native.AllocateDeviceConfig(Capability, SampleFormat, (uint)Channels, (uint)SampleRate,
            _audioCallback ??= AudioCallback,
            playbackDeviceId,
            captureDeviceId);

        _device = Native.AllocateDevice();
        var result = Native.DeviceInit(_context, deviceConfig, _device);
        Native.Free(deviceConfig);

        if (result != Result.Success)
        {
            Native.Free(_device);
            _device = nint.Zero;
            throw new InvalidOperationException($"Unable to init device. {result}");
        }

        result = Native.DeviceStart(_device);
        if (result != Result.Success)
        {
            CleanupCurrentDevice();
            throw new InvalidOperationException($"Unable to start device. {result}");
        }

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


    private void AudioCallback(IntPtr _, IntPtr output, IntPtr input, uint length)
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
    public override ISoundEncoder CreateEncoder(Stream stream, EncodingFormat encodingFormat,
        SampleFormat sampleFormat, int encodingChannels, int sampleRate)
    {
        return new MiniAudioEncoder(stream, encodingFormat, sampleFormat, encodingChannels, sampleRate);
    }

    /// <inheritdoc />
    public override ISoundDecoder CreateDecoder(Stream stream)
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
        var result = Native.GetDevices(_context, out var pPlaybackDevices, out var pCaptureDevices,
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
    }
}