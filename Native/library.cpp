#define MINIAUDIO_IMPLEMENTATION

#include "library.h"


#define LIBRARY_H

// Helper macro for  memory allocation.
#define sf_create(t) (t*) ma_malloc(sizeof(t), NULL)

extern "C" {
// Frees a structure allocated with sf_create().
MA_API void sf_free(void *ptr) {
    return ma_free(ptr, nullptr);
}

// Allocate memory for a decoder struct.
MA_API ma_decoder *sf_allocate_decoder() {
    return sf_create(ma_decoder);
}

// Allocate memory for an encoder struct.
MA_API ma_encoder *sf_allocate_encoder() {
    return sf_create(ma_encoder);
}

// Allocate memory for a device struct.
MA_API ma_device *sf_allocate_device() {
    return sf_create(ma_device);
}

// Allocate memory for a context struct.
MA_API ma_context *sf_allocate_context() {
    return sf_create(ma_context);
}

// Allocate memory for a device configuration struct.
MA_API ma_device_config *sf_allocate_device_config(const ma_device_type deviceType, const ma_format format,
                                                   const int channels, const int sampleRate,
                                                   const ma_device_data_proc *dataCallback,
                                                   const ma_device_id *playbackDeviceId,
                                                   const ma_device_id *captureDeviceId) {
    auto *config = sf_create(ma_device_config);
    if (config == nullptr) {
        return nullptr;
    }
    MA_ZERO_OBJECT(config);

    *config = ma_device_config_init(deviceType);

    // Configure device type and callback
    config->dataCallback = *dataCallback;

    // Configure to use stereo signed 16 bit audio
    config->sampleRate = sampleRate;
    config->playback.format = format;
    config->playback.channels = channels;
    config->capture.format = format;
    config->capture.channels = channels;
    config->capture.shareMode  = ma_share_mode_shared;

    // Set device IDs
    config->playback.pDeviceID = playbackDeviceId;
    config->capture.pDeviceID = captureDeviceId;

    // Optimize for low latency
    config->performanceProfile = ma_performance_profile_low_latency;
    config->wasapi.noAutoConvertSRC = true;

    return config;
}

// Allocate memory for a decoder configuration struct.
MA_API ma_decoder_config *sf_allocate_decoder_config(const ma_format outputFormat, const int outputChannels,
                                                     const int outputSampleRate) {
    auto *pConfig = sf_create(ma_decoder_config);
    if (pConfig == nullptr) {
        return nullptr;
    }

    MA_ZERO_OBJECT(pConfig);
    *pConfig = ma_decoder_config_init(outputFormat, outputChannels, outputSampleRate);

    return pConfig;
}

// Allocate memory for an encoder configuration struct.
MA_API ma_encoder_config *sf_allocate_encoder_config(const ma_encoding_format encodingFormat, const ma_format format,
                                                     const int channels, const int sampleRate) {
    auto *pConfig = sf_create(ma_encoder_config);
    if (pConfig == nullptr) {
        return nullptr;
    }

    MA_ZERO_OBJECT(pConfig);
    *pConfig = ma_encoder_config_init(encodingFormat, format, channels, sampleRate);

    return pConfig;
}

ma_result sf_get_devices(ma_context *context, sf_device_info **ppPlaybackDeviceInfos,
                         sf_device_info **ppCaptureDeviceInfos, ma_uint32 *pPlaybackDeviceCount,
                         ma_uint32 *pCaptureDeviceCount) {
    ma_device_info *pPlaybackDevices = nullptr;
    ma_device_info *pCaptureDevices = nullptr;

    const auto result = ma_context_get_devices(context,
                                               &pPlaybackDevices,
                                               pPlaybackDeviceCount,
                                               &pCaptureDevices,
                                               pCaptureDeviceCount);

    if (result != MA_SUCCESS || *pPlaybackDeviceCount == 0 && *pCaptureDeviceCount == 0) {
        return result;
    }

    sf_device_info *playbackDeviceInfos = nullptr; // Local variables, better names
    sf_device_info *captureDeviceInfos = nullptr;

    if (*pPlaybackDeviceCount > 0) {
        playbackDeviceInfos = static_cast<sf_device_info *>(ma_malloc(sizeof(sf_device_info) * *pPlaybackDeviceCount,
                                                                      nullptr));
        if (playbackDeviceInfos == nullptr) {
            sf_free(pPlaybackDevices);
            sf_free(pCaptureDevices);
            return MA_OUT_OF_MEMORY;
        }
    }

    if (*pCaptureDeviceCount > 0) {
        captureDeviceInfos = static_cast<sf_device_info *>(ma_malloc(sizeof(sf_device_info) * *pCaptureDeviceCount,
                                                                     nullptr));
        if (captureDeviceInfos == nullptr) {
            sf_free(pPlaybackDevices);
            sf_free(pCaptureDevices);
            sf_free(playbackDeviceInfos);
            return MA_OUT_OF_MEMORY;
        }
    }

    for (ma_uint32 iDevice = 0; iDevice < *pPlaybackDeviceCount; ++iDevice) {
        auto *pPlaybackDeviceInfo = &pPlaybackDevices[iDevice];
        sf_device_info deviceInfo;
        deviceInfo.id = &pPlaybackDeviceInfo->id;
        strncpy(deviceInfo.name, pPlaybackDeviceInfo->name, MA_MAX_DEVICE_NAME_LENGTH); // Use strncpy
        deviceInfo.name[MA_MAX_DEVICE_NAME_LENGTH] = '\0'; // Ensure null termination
        deviceInfo.isDefault = pPlaybackDeviceInfo->isDefault;
        deviceInfo.nativeDataFormatCount = pPlaybackDeviceInfo->nativeDataFormatCount;

        if (deviceInfo.nativeDataFormatCount > 0) {
            // Allocate memory for nativeDataFormats
            deviceInfo.nativeDataFormats = static_cast<native_data_format *>(ma_malloc(
                sizeof(native_data_format) * pPlaybackDeviceInfo->nativeDataFormatCount,
                nullptr));

            // Copy all nativeDataFormats
            for (ma_uint32 iFormat = 0; iFormat < pPlaybackDeviceInfo->nativeDataFormatCount; ++iFormat) {
                deviceInfo.nativeDataFormats[iFormat].format = pPlaybackDeviceInfo->nativeDataFormats[iFormat].format;
                deviceInfo.nativeDataFormats[iFormat].channels = pPlaybackDeviceInfo->nativeDataFormats[iFormat].
                        channels;
                deviceInfo.nativeDataFormats[iFormat].sampleRate = pPlaybackDeviceInfo->nativeDataFormats[iFormat].
                        sampleRate;
                deviceInfo.nativeDataFormats[iFormat].flags = pPlaybackDeviceInfo->nativeDataFormats[iFormat].flags;
            }
        }

        if (playbackDeviceInfos != nullptr)
            playbackDeviceInfos[iDevice] = deviceInfo;
    }

    for (ma_uint32 iDevice = 0; iDevice < *pCaptureDeviceCount; ++iDevice) {
        auto *pCaptureDeviceInfo = &pCaptureDevices[iDevice];
        sf_device_info deviceInfo;
        deviceInfo.id = &pCaptureDeviceInfo->id;
        strncpy(deviceInfo.name, pCaptureDeviceInfo->name, MA_MAX_DEVICE_NAME_LENGTH);
        deviceInfo.name[MA_MAX_DEVICE_NAME_LENGTH] = '\0'; // Ensure null termination
        deviceInfo.isDefault = pCaptureDeviceInfo->isDefault;
        deviceInfo.nativeDataFormatCount = pCaptureDeviceInfo->nativeDataFormatCount;

        if (deviceInfo.nativeDataFormatCount > 0) {
            // Allocate memory for nativeDataFormats
            deviceInfo.nativeDataFormats = static_cast<native_data_format *>(ma_malloc(
                sizeof(native_data_format) * pCaptureDeviceInfo->nativeDataFormatCount,
                nullptr));

            // Copy all nativeDataFormats
            for (ma_uint32 iFormat = 0; iFormat < pCaptureDeviceInfo->nativeDataFormatCount; ++iFormat) {
                deviceInfo.nativeDataFormats[iFormat].format = pCaptureDeviceInfo->nativeDataFormats[iFormat].format;
                deviceInfo.nativeDataFormats[iFormat].channels = pCaptureDeviceInfo->nativeDataFormats[iFormat].
                        channels;
                deviceInfo.nativeDataFormats[iFormat].sampleRate = pCaptureDeviceInfo->nativeDataFormats[iFormat].
                        sampleRate;
                deviceInfo.nativeDataFormats[iFormat].flags = pCaptureDeviceInfo->nativeDataFormats[iFormat].flags;
            }
        }

        if (captureDeviceInfos != nullptr)
            captureDeviceInfos[iDevice] = deviceInfo;
    }

    *ppPlaybackDeviceInfos = playbackDeviceInfos;
    *ppCaptureDeviceInfos = captureDeviceInfos;

    return result;
}
} // End of extern "C" block
