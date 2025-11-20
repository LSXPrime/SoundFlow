using System.Runtime.InteropServices;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Backends.MiniAudio.Structs;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace SoundFlow.Backends.MiniAudio.Devices;

internal delegate void OnProcessCallback(nint pOutput, nint pInput, uint frameCount, MiniAudioDevice device);

internal sealed class MiniAudioDevice : IDisposable
{
    private readonly nint _device;
    private readonly OnProcessCallback _onProcess;

    public DeviceInfo? Info { get; }
    public Capability Capability { get; }
    public AudioFormat Format { get; }
    public MiniAudioEngine Engine { get; }

    public MiniAudioDevice(AudioDevice owner, nint context, DeviceInfo? info, AudioFormat format, DeviceConfig config,
        OnProcessCallback onProcess)
    {
        if (config is not MiniAudioDeviceConfig miniAudioDeviceConfig)
            throw new ArgumentException($"config must be of type {typeof(MiniAudioDeviceConfig)}");
        
        Info = info;
        Format = format;
        _onProcess = onProcess;
        Engine = (MiniAudioEngine)owner.Engine;

        Capability = owner is AudioCaptureDevice
            ? miniAudioDeviceConfig is { Capture.IsLoopback: true } ? Capability.Loopback : Capability.Record
            : Capability.Playback;

        var configHandles = new List<nint>();

        try
        {
            var pSfConfig = MarshalConfig(
                miniAudioDeviceConfig, configHandles);

            var deviceConfig = Native.AllocateDeviceConfig(
                Capability,
                (uint)Format.SampleRate,
                MiniAudioEngine.DataCallback,
                pSfConfig
            );

            _device = Native.AllocateDevice();
            var result = Native.DeviceInit(context, deviceConfig, _device);
            Native.Free(deviceConfig);

            if (result != MiniAudioResult.Success)
            {
                Native.Free(_device);
                throw new InvalidOperationException($"Unable to init device {info?.Name ?? "Default Device"}. Result: {result}");
            }
            
            MiniAudioEngine.RegisterEngineHandle(_device, Engine);
        }
        finally
        {
            // Free all marshaled memory
            foreach (var handle in configHandles)
            {
                Marshal.FreeHGlobal(handle);
            }
        }

        Engine.RegisterDevice(_device, this);
    }

    private nint MarshalConfig(MiniAudioDeviceConfig maConfig, List<nint> handles)
    {
        var mainDto = new SfDeviceConfig
        {
            PeriodSizeInFrames = maConfig.PeriodSizeInFrames,
            PeriodSizeInMilliseconds = maConfig.PeriodSizeInMilliseconds,
            Periods = maConfig.Periods,
            NoPreSilencedOutputBuffer = maConfig.NoPreSilencedOutputBuffer,
            NoClip = maConfig.NoClip,
            NoDisableDenormals = maConfig.NoDisableDenormals,
            NoFixedSizedCallback = maConfig.NoFixedSizedCallback,
            Playback = MarshalStruct(new SfDeviceSubConfig
            {
                Format = Format.Format,
                Channels = (uint)Format.Channels,
                pDeviceID = Info?.Id ?? nint.Zero,
                ShareMode = maConfig.Playback.ShareMode
            }, handles),
            Capture = MarshalStruct(new SfDeviceSubConfig
            {
                Format = Format.Format,
                Channels = (uint)Format.Channels,
                pDeviceID = Info?.Id ?? nint.Zero,
                ShareMode = maConfig.Capture.ShareMode
            }, handles)
        };

        if (maConfig.Wasapi != null)
        {
            var dto = new SfWasapiConfig
            {
                Usage = maConfig.Wasapi.Usage,
                NoAutoConvertSRC = maConfig.Wasapi.NoAutoConvertSRC,
                NoDefaultQualitySRC = maConfig.Wasapi.NoDefaultQualitySRC,
                NoAutoStreamRouting = maConfig.Wasapi.NoAutoStreamRouting,
                NoHardwareOffloading = maConfig.Wasapi.NoHardwareOffloading
            };
            mainDto.Wasapi = MarshalStruct(dto, handles);
        }

        if (maConfig.CoreAudio != null)
        {
            mainDto.CoreAudio =
                MarshalStruct(new SfCoreAudioConfig
                {
                    AllowNominalSampleRateChange = (uint)(maConfig.CoreAudio.AllowNominalSampleRateChange ? 1 : 0)
                }, handles);
        }

        if (maConfig.Alsa != null)
        {
            mainDto.Alsa = MarshalStruct(new SfAlsaConfig
            {
                NoMMap = ToUInt(maConfig.Alsa.NoMMap),
                NoAutoFormat = ToUInt(maConfig.Alsa.NoAutoFormat),
                NoAutoChannels = ToUInt(maConfig.Alsa.NoAutoChannels),
                NoAutoResample = ToUInt(maConfig.Alsa.NoAutoResample)
            }, handles);
        }

        if (maConfig.Pulse != null)
        {
            mainDto.Pulse = MarshalStruct(new SfPulseConfig
            {
                pStreamNamePlayback = Marshal.StringToHGlobalAnsi(maConfig.Pulse.StreamNamePlayback),
                pStreamNameCapture = Marshal.StringToHGlobalAnsi(maConfig.Pulse.StreamNameCapture)
            }, handles);
        }

        if (maConfig.OpenSL != null)
        {
            mainDto.OpenSL = MarshalStruct(new SfOpenSlConfig
            {
                StreamType = maConfig.OpenSL.StreamType,
                RecordingPreset = maConfig.OpenSL.RecordingPreset
            }, handles);
        }

        if (maConfig.AAudio != null)
        {
            mainDto.AAudio = MarshalStruct(
                new SfAAudioConfig
                {
                    Usage = maConfig.AAudio.Usage,
                    ContentType = maConfig.AAudio.ContentType,
                    InputPreset = maConfig.AAudio.InputPreset,
                    AllowedCapturePolicy = maConfig.AAudio.AllowedCapturePolicy
                }, handles);
        }

        return MarshalStruct(mainDto, handles);
    }

    private static nint MarshalStruct<T>(T structure, List<nint> handles) where T : struct
    {
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        Marshal.StructureToPtr(structure, ptr, false);
        handles.Add(ptr);
        return ptr;
    }

    private static uint ToUInt(bool value) => (uint)(value ? 1 : 0);


    public void Start() => Native.DeviceStart(_device);

    public void Stop() => Native.DeviceStop(_device);

    public void Process(nint pOutput, nint pInput, uint frameCount)
    {
        _onProcess(pOutput, pInput, frameCount, this);
    }

    public void Dispose()
    {
        Stop();
        
        // Remove the device from the engine's instance map.
        Engine.UnregisterDevice(_device);
        MiniAudioEngine.UnregisterEngineHandle(_device);

        Native.DeviceUninit(_device);
        Native.Free(_device);
    }
}