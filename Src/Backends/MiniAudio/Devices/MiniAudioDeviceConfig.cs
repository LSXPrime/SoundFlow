using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio.Enums;

namespace SoundFlow.Backends.MiniAudio.Devices;

/// <summary>
/// Represents detailed configuration for a MiniAudio device, allowing fine-grained control
/// over general and backend-specific settings.
/// </summary>
public class MiniAudioDeviceConfig : DeviceConfig
{
    /// <summary>
    /// Gets or sets the desired period size in frames per channel. Takes precedence over PeriodSizeInMilliseconds.
    /// Set to 0 to use the backend's default.
    /// </summary>
    /// <example>10ms * 48khz = 480 frames, If channels are set to 2, then It will automatically be 960 frames</example>
    public uint PeriodSizeInFrames { get; set; }

    /// <summary>
    /// Gets or sets the desired period size in milliseconds per channel.
    /// Set to 0 to use the backend's default.
    /// </summary>
    /// <example>10ms, If channels are set to 2, then It will automatically be 20ms, asking the player for a larger buffer</example>
    public uint PeriodSizeInMilliseconds { get; set; }

    /// <summary>
    /// Gets or sets the number of periods to use for the device's buffer.
    /// Set to 0 to use the backend's default.
    /// </summary>
    public uint Periods { get; set; }
    
    /// <summary>
    /// When set to true, the contents of the output buffer passed into the data callback
    /// will be left undefined rather than initialized to silence.
    /// </summary>
    public bool NoPreSilencedOutputBuffer { get; set; }

    /// <summary>
    /// When set to true, the contents of the output buffer passed into the data callback will not be
    /// clipped after returning. Only applies when the playback sample format is F32.
    /// </summary>
    public bool NoClip { get; set; }

    /// <summary>
    /// When set to true, the backend will not attempt to disable denormal floating point numbers,
    /// which can improve performance at the risk of precision loss.
    /// </summary>
    public bool NoDisableDenormals { get; set; }

    /// <summary>
    /// Disables strict fixed-sized data callbacks. Setting this to true will result in the period size
    /// being treated only as a hint to the backend. This is an optimization for those who don't need fixed sized callbacks.
    /// </summary>
    public bool NoFixedSizedCallback { get; set; }

    /// <summary>
    /// Gets or sets the configuration specific to playback.
    /// </summary>
    public DeviceSubConfig Playback { get; set; } = new()
    {
        ShareMode = ShareMode.Shared
    };

    /// <summary>
    /// Gets or sets the configuration specific to capture.
    /// </summary>
    public DeviceSubConfig Capture { get; set; } = new()
    {
        ShareMode = ShareMode.Shared
    };

    /// <summary>
    /// Gets or sets the configuration specific to the WASAPI backend. This is only used on Windows.
    /// </summary>
    public WasapiSettings? Wasapi { get; set; }

    /// <summary>
    /// Gets or sets the configuration specific to the CoreAudio backend. This is only used on macOS/iOS.
    /// </summary>
    public CoreAudioSettings? CoreAudio { get; set; }

    /// <summary>
    /// Gets or sets the configuration specific to the ALSA backend. This is only used on Linux.
    /// </summary>
    public AlsaSettings? Alsa { get; set; }
    
    /// <summary>
    /// Gets or sets the configuration specific to the PulseAudio backend. This is only used on Linux.
    /// </summary>
    public PulseSettings? Pulse { get; set; }
    
    /// <summary>
    /// Gets or sets the configuration specific to the OpenSL ES backend. This is only used on Android.
    /// </summary>
    public OpenSlSettings? OpenSL { get; set; }
    
    /// <summary>
    /// Gets or sets the configuration specific to the AAudio backend. This is only used on Android.
    /// </summary>
    public AAudioSettings? AAudio { get; set; }
}

/// <summary>
/// Contains settings for a specific direction (playback or capture).
/// </summary>
public class DeviceSubConfig
{
    /// <summary>
    /// The sharing mode for the device. Use Exclusive for lowest latency if available.
    /// </summary>
    public ShareMode ShareMode { get; set; } = ShareMode.Shared;
    
    /// <summary>
    /// Gets or sets a value indicating whether the device is a loopback device (Applies only to capture devices config).
    /// </summary>
    [field: NonSerialized]
    internal bool IsLoopback { get; set; }
}

/// <summary>
/// Contains settings specific to the WASAPI audio backend on Windows.
/// </summary>
public class WasapiSettings
{
    /// <summary>
    /// Gets or sets the usage scenario for the audio stream.
    /// This hints to the operating system about the stream's purpose, potentially affecting
    /// system-level prioritization and processing.
    /// Maps to `ma_wasapi_usage` in MiniAudio's `ma_device_config.wasapi.usage`.
    /// </summary>
    public WasapiUsage Usage { get; set; } = WasapiUsage.Default;

    /// <summary>
    /// Gets or sets a value indicating whether to disable automatic sample rate conversion (SRC) by WASAPI.
    /// When true, MiniAudio will perform the SRC instead of the OS.
    /// Maps to `ma_device_config.wasapi.noAutoConvertSRC`.
    /// </summary>
    public bool NoAutoConvertSRC { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to disable the use of `AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY`.
    /// When true, WASAPI will not use its default SRC quality.
    /// Maps to `ma_device_config.wasapi.noDefaultQualitySRC`.
    /// </summary>
    public bool NoDefaultQualitySRC { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to disable automatic stream routing by WASAPI.
    /// Maps to `ma_device_config.wasapi.noAutoStreamRouting`.
    /// </summary>
    public bool NoAutoStreamRouting { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to disable WASAPI's hardware offloading feature.
    /// Maps to `ma_device_config.wasapi.noHardwareOffloading`.
    /// </summary>
    public bool NoHardwareOffloading { get; set; }
}

/// <summary>
/// Contains settings specific to the CoreAudio backend on macOS and iOS.
/// </summary>
public class CoreAudioSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether to allow CoreAudio to change the sample rate
    /// at the operating system level. This setting is typically for desktop macOS.
    /// Maps to `ma_device_config.coreaudio.allowNominalSampleRateChange`.
    /// </summary>
    public bool AllowNominalSampleRateChange { get; set; }
}

/// <summary>
/// Contains settings specific to the ALSA audio backend on Linux.
/// </summary>
public class AlsaSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether to disable Memory Map (MMap) mode for ALSA.
    /// Maps to `ma_device_config.alsa.noMMap`.
    /// </summary>
    public bool NoMMap { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to open the ALSA device with `SND_PCM_NO_AUTO_FORMAT`.
    /// This disables automatic format conversion by ALSA.
    /// Maps to `ma_device_config.alsa.noAutoFormat`.
    /// </summary>
    public bool NoAutoFormat { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to open the ALSA device with `SND_PCM_NO_AUTO_CHANNELS`.
    /// This disables automatic channel count conversion by ALSA.
    /// Maps to `ma_device_config.alsa.noAutoChannels`.
    /// </summary>
    public bool NoAutoChannels { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to open the ALSA device with `SND_PCM_NO_AUTO_RESAMPLE`.
    /// This disables automatic resampling by ALSA.
    /// Maps to `ma_device_config.alsa.noAutoResample`.
    /// </summary>
    public bool NoAutoResample { get; set; }
}

/// <summary>
/// Contains settings specific to the PulseAudio backend on Linux.
/// </summary>
public class PulseSettings
{
    /// <summary>
    /// Gets or sets the stream name for playback within PulseAudio.
    /// Maps to `ma_device_config.pulse.pStreamNamePlayback`.
    /// </summary>
    public string? StreamNamePlayback { get; set; }

    /// <summary>
    /// Gets or sets the stream name for capture within PulseAudio.
    /// Maps to `ma_device_config.pulse.pStreamNameCapture`.
    /// </summary>
    public string? StreamNameCapture { get; set; }
}

/// <summary>
/// Contains settings specific to the OpenSL ES backend on Android.
/// </summary>
public class OpenSlSettings
{
    /// <summary>
    /// Gets or sets the stream type for OpenSL ES.
    /// Maps to `ma_opensl_stream_type` in MiniAudio's `ma_device_config.opensl.streamType`.
    /// </summary>
    public OpenSlStreamType StreamType { get; set; }

    /// <summary>
    /// Gets or sets the recording preset for OpenSL ES.
    /// Maps to `ma_opensl_recording_preset` in MiniAudio's `ma_device_config.opensl.recordingPreset`.
    /// </summary>
    public OpenSlRecordingPreset RecordingPreset { get; set; }
}

/// <summary>
/// Contains settings specific to the AAudio backend on Android.
/// </summary>
public class AAudioSettings
{
    /// <summary>
    /// Gets or sets the usage scenario for AAudio.
    /// Maps to `ma_aaudio_usage` in MiniAudio's `ma_device_config.aaudio.usage`.
    /// </summary>
    public AAudioUsage Usage { get; set; }

    /// <summary>
    /// Gets or sets the content type for AAudio.
    /// Maps to `ma_aaudio_content_type` in MiniAudio's `ma_device_config.aaudio.contentType`.
    /// </summary>
    public AAudioContentType ContentType { get; set; }

    /// <summary>
    /// Gets or sets the input preset for AAudio.
    /// Maps to `ma_aaudio_input_preset` in MiniAudio's `ma_device_config.aaudio.inputPreset`.
    /// </summary>
    public AAudioInputPreset InputPreset { get; set; }

    /// <summary>
    /// Gets or sets the allowed capture policy for AAudio.
    /// Maps to `ma_aaudio_allowed_capture_policy` in MiniAudio's `ma_device_config.aaudio.allowedCapturePolicy`.
    /// </summary>
    public AAudioAllowedCapturePolicy AllowedCapturePolicy { get; set; }
}