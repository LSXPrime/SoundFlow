// library.h
// A facade for the MiniAudio library, providing C-compatible functions
// for memory allocation and basic structure initialization.

#ifndef LIBRARY_H
#define LIBRARY_H

#include "miniaudio/miniaudio.h"

#ifdef __cplusplus
extern "C" {
#endif

struct native_data_format {
    ma_format format;
    ma_uint32 channels;
    ma_uint32 sampleRate;
    ma_uint32 flags;
};

struct sf_device_info {
    ma_device_id *id;
    char name[MA_MAX_DEVICE_NAME_LENGTH + 1]; // MA_MAX_DEVICE_NAME_LENGTH is 255
    ma_bool8 isDefault;
    ma_uint32 nativeDataFormatCount;
    struct native_data_format *nativeDataFormats;
};

struct sf_WasapiConfig {
    ma_wasapi_usage usage;
    ma_bool8 noAutoConvertSRC;
    ma_bool8 noDefaultQualitySRC;
    ma_bool8 noAutoStreamRouting;
    ma_bool8 noHardwareOffloading;
};

struct sf_CoreAudioConfig {
    ma_bool32 allowNominalSampleRateChange;
};

struct sf_AlsaConfig {
    ma_bool32 noMMap;
    ma_bool32 noAutoFormat;
    ma_bool32 noAutoChannels;
    ma_bool32 noAutoResample;
};

struct sf_PulseConfig {
    const char *pStreamNamePlayback;
    const char *pStreamNameCapture;
};

struct sf_OpenSlConfig {
    ma_opensl_stream_type streamType;
    ma_opensl_recording_preset recordingPreset;
};

struct sf_AAudioConfig {
    ma_aaudio_usage usage;
    ma_aaudio_content_type contentType;
    ma_aaudio_input_preset inputPreset;
    ma_aaudio_allowed_capture_policy allowedCapturePolicy;
};

struct sf_DeviceSubConfig {
    ma_format format;
    ma_uint32 channels;
    const ma_device_id *pDeviceID;
    ma_share_mode shareMode;
};

// The main config DTO that C# will marshal
struct sf_DeviceConfig {
    ma_uint32 periodSizeInFrames;
    ma_uint32 periodSizeInMilliseconds;
    ma_uint32 periods;
    ma_bool8 noPreSilencedOutputBuffer;
    ma_bool8 noClip;
    ma_bool8 noDisableDenormals;
    ma_bool8 noFixedSizedCallback;

    struct sf_DeviceSubConfig *playback;
    struct sf_DeviceSubConfig *capture;

    struct sf_WasapiConfig *wasapi;
    struct sf_CoreAudioConfig *coreaudio;
    struct sf_AlsaConfig *alsa;
    struct sf_PulseConfig *pulse;
    struct sf_OpenSlConfig *opensl;
    struct sf_AAudioConfig *aaudio;
};

// Frees a structure allocated with sf_create().
MA_API void sf_free(void *ptr);

// Allocate memory for a decoder struct.
MA_API ma_decoder *sf_allocate_decoder(void);

// Allocate memory for an encoder struct.
MA_API ma_encoder *sf_allocate_encoder(void);

// Allocate memory for a device struct.
MA_API ma_device *sf_allocate_device(void);

// Allocate memory for a context struct.
MA_API ma_context *sf_allocate_context(void);

// Allocate memory for a device configuration struct.
MA_API ma_device_config *sf_allocate_device_config(ma_device_type deviceType, ma_uint32 sampleRate,
                                                   ma_device_data_proc onData, const struct sf_DeviceConfig *pSfConfig);

// Allocate memory for a decoder configuration struct.
MA_API ma_decoder_config *sf_allocate_decoder_config(ma_format outputFormat, ma_uint32 outputChannels,
                                                     ma_uint32 outputSampleRate);

// Allocate memory for an encoder configuration struct.
MA_API ma_encoder_config *sf_allocate_encoder_config(ma_format format,
                                                     ma_uint32 channels, ma_uint32 sampleRate);

MA_API ma_result sf_get_devices(ma_context *context, struct sf_device_info **ppPlaybackDeviceInfos,
                                struct sf_device_info **ppCaptureDeviceInfos, ma_uint32 *pPlaybackDeviceCount,
                                ma_uint32 *pCaptureDeviceCount);

MA_API void sf_free_device_infos(struct sf_device_info* deviceInfos, ma_uint32 count);

MA_API ma_backend sf_context_get_backend(const ma_context* pContext);

#ifdef __cplusplus
}
#endif

#endif // LIBRARY_H