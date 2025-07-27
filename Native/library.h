// library.h
// Simple facade for the MiniAudio library, providing C-compatible functions
// for memory allocation and basic structure initialization.

#ifndef LIBRARY_H
#define LIBRARY_H

#include <vector>

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
MA_API ma_device_config *sf_allocate_device_config(const ma_device_type deviceType, const ma_format format,
                                                       const int channels, const int sampleRate,
                                                       const ma_device_data_proc *dataCallback,
                                                       const ma_device_id *playbackDeviceId,
                                                       const ma_device_id *captureDeviceId);

// Allocate memory for a decoder configuration struct.
MA_API ma_decoder_config *sf_allocate_decoder_config(ma_format outputFormat, int outputChannels,
                                                     int outputSampleRate);

// Allocate memory for an encoder configuration struct.
MA_API ma_encoder_config *sf_allocate_encoder_config(ma_encoding_format encodingFormat, ma_format format,
                                                     int channels, int sampleRate);

MA_API ma_result sf_get_devices(ma_context *context, sf_device_info **ppPlaybackDeviceInfos,
                                sf_device_info **ppCaptureDeviceInfos, ma_uint32 *pPlaybackDeviceCount,
                                ma_uint32 *pCaptureDeviceCount);
}

#endif // LIBRARY_H
