#if BROWSER
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Enums;

namespace SoundFlow.Backends.MiniAudio;

[SupportedOSPlatform("browser")]
public static partial class Native
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AudioCallback(nint device, nint output, nint input, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Result DecoderRead(nint pDecoder, nint pBufferOut, ulong bytesToRead, nint pBytesRead);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Result DecoderSeek(nint pDecoder, long byteOffset, SeekPoint origin);

    private const string JsModulePrefix = "globalThis.";

    #region Encoder

    [JSImport($"{JsModulePrefix}ma_encoder_init_file")]
    public static partial int EncoderInitFile(string filePath, nint pConfig, nint pEncoder);

    [JSImport($"{JsModulePrefix}ma_encoder_uninit")]
    public static partial void EncoderUninit(nint pEncoder);

    [JSImport($"{JsModulePrefix}ma_encoder_write_pcm_frames")]
    public static partial int EncoderWritePcmFrames(nint pEncoder, nint pFramesIn, [JSMarshalAs<JSType.Any>] object frameCount,
        nint pFramesWritten);

    #endregion

    #region Decoder

    [JSImport($"{JsModulePrefix}ma_decoder_init")]
    public static partial int DecoderInit(nint onRead, nint onSeek, nint pUserData,
        nint pConfig, nint pDecoder);

    [JSImport($"{JsModulePrefix}ma_decoder_uninit")]
    public static partial int DecoderUninit(nint pDecoder);

    [JSImport($"{JsModulePrefix}ma_decoder_read_pcm_frames")]
    public static partial int DecoderReadPcmFrames(nint decoder, nint framesOut, [JSMarshalAs<JSType.Any>] object frameCount,
        nint framesRead);

    [JSImport($"{JsModulePrefix}ma_decoder_seek_to_pcm_frame")]
    public static partial int DecoderSeekToPcmFrame(nint decoder, [JSMarshalAs<JSType.Any>] object frame);

    [JSImport($"{JsModulePrefix}ma_decoder_get_length_in_pcm_frames")]
    public static partial int DecoderGetLengthInPcmFrames(nint decoder, nint length);

    #endregion

    #region Context

    [JSImport($"{JsModulePrefix}ma_context_init")]
    public static partial int ContextInit(nint backends, [JSMarshalAs<JSType.Any>]object backendCount, nint config, nint context);

    [JSImport($"{JsModulePrefix}ma_context_uninit")]
    public static partial void ContextUninit(nint context);

    #endregion

    #region Device

    [JSImport($"{JsModulePrefix}sf_get_devices")]
    public static partial int GetDevices(nint context, nint pPlaybackDevices, nint pCaptureDevices, nint playbackDeviceCount, nint captureDeviceCount);

    [JSImport($"{JsModulePrefix}ma_device_init")]
    public static partial int DeviceInit(nint context, nint config, nint device);

    [JSImport($"{JsModulePrefix}ma_device_uninit")]
    public static partial void DeviceUninit(nint device);

    [JSImport($"{JsModulePrefix}ma_device_start")]
    public static partial int DeviceStart(nint device);

    [JSImport($"{JsModulePrefix}ma_device_stop")]
    public static partial int DeviceStop(nint device);

    #endregion

    #region Allocations

    [JSImport($"{JsModulePrefix}sf_allocate_encoder")]
    public static partial nint AllocateEncoder();

    [JSImport($"{JsModulePrefix}sf_allocate_decoder")]
    public static partial nint AllocateDecoder();

    [JSImport($"{JsModulePrefix}sf_allocate_context")]
    public static partial nint AllocateContext();

    [JSImport($"{JsModulePrefix}sf_allocate_device")]
    public static partial nint AllocateDevice();

    [JSImport($"{JsModulePrefix}sf_allocate_decoder_config")]
    public static partial nint AllocateDecoderConfig(int format, int channels, int sampleRate);

    [JSImport($"{JsModulePrefix}sf_allocate_encoder_config")]
    public static partial nint AllocateEncoderConfig(int encodingFormat, int format, int channels, int sampleRate);

    [JSImport($"{JsModulePrefix}sf_allocate_device_config")]
    public static partial nint AllocateDeviceConfig(int capabilityType, int format, int channels,
        int sampleRate, nint dataCallback, nint playbackDevice, nint captureDevice);

    #endregion

    #region Utils

    [JSImport($"{JsModulePrefix}sf_free")]
    public static partial void Free(nint ptr);

    #endregion
}
#endif