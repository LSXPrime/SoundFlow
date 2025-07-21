// library.h
// Simple facade for the MiniAudio library, providing C-compatible functions
// for memory allocation and basic structure initialization.

#ifndef LIBRARY_H
#define LIBRARY_H

#include "Submodules/miniaudio/miniaudio.h"

struct native_data_format {
    ma_format format;
    ma_uint32 channels;
    ma_uint32 sampleRate;
    ma_uint32 flags;
};

struct sf_device_info {
    ma_device_id *id;
    char name[MA_MAX_DEVICE_NAME_LENGTH + 1]; // MA_MAX_DEVICE_NAME_LENGTH is 255
    bool isDefault;
    ma_uint32 nativeDataFormatCount;
    native_data_format *nativeDataFormats;
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

    sf_DeviceSubConfig *playback;
    sf_DeviceSubConfig *capture;

    sf_WasapiConfig *wasapi;
    sf_CoreAudioConfig *coreaudio;
    sf_AlsaConfig *alsa;
    sf_PulseConfig *pulse;
    sf_OpenSlConfig *opensl;
    sf_AAudioConfig *aaudio;
};

extern "C" {
// Frees a structure allocated with sf_create().
MA_API void sf_free(void *ptr);

// Allocate memory for a decoder struct.
MA_API ma_decoder *sf_allocate_decoder();

// Allocate memory for an encoder struct.
MA_API ma_encoder *sf_allocate_encoder();

// Allocate memory for a device struct.
MA_API ma_device *sf_allocate_device();

// Allocate memory for a context struct.
MA_API ma_context *sf_allocate_context();

// Allocate memory for a device configuration struct.
MA_API ma_device_config *sf_allocate_device_config(ma_device_type deviceType, ma_uint32 sampleRate,
                                                   ma_device_data_proc onData, const sf_DeviceConfig *pSfConfig);

// Allocate memory for a decoder configuration struct.
MA_API ma_decoder_config *sf_allocate_decoder_config(ma_format outputFormat, ma_uint32 outputChannels,
                                                     ma_uint32 outputSampleRate);

// Allocate memory for an encoder configuration struct.
MA_API ma_encoder_config *sf_allocate_encoder_config(ma_encoding_format encodingFormat, ma_format format,
                                                     ma_uint32 channels, ma_uint32 sampleRate);

MA_API ma_result sf_get_devices(ma_context *context, sf_device_info **ppPlaybackDeviceInfos,
                                sf_device_info **ppCaptureDeviceInfos, ma_uint32 *pPlaybackDeviceCount,
                                ma_uint32 *pCaptureDeviceCount);

MA_API void sf_free_device_infos(sf_device_info* deviceInfos, ma_uint32 count);
}

#endif // LIBRARY_H