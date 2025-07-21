#define MINIAUDIO_IMPLEMENTATION

#include "library.h"

// Helper macro for memory allocation
#define sf_create(t) static_cast<t*>(ma_malloc(sizeof(t), nullptr))

// Helper function to safely copy strings with bounds checking
static void sf_safe_strcpy(char* dest, const char* src) {
    if (dest == nullptr) {
        return;
    }

    size_t srcLen = strnlen(src, 256 - 1);
    memcpy(dest, src, srcLen);
    dest[srcLen] = '\0';
}

// Helper function to create device info structure - eliminates code duplication
static sf_device_info sf_create_device_info(const ma_device_info* pDeviceInfo) {
    sf_device_info deviceInfo;

    if (pDeviceInfo == nullptr) {
        memset(&deviceInfo, 0, sizeof(deviceInfo));
        return deviceInfo;
    }

    deviceInfo.id = const_cast<ma_device_id*>(&pDeviceInfo->id);
    sf_safe_strcpy(deviceInfo.name, pDeviceInfo->name);
    deviceInfo.isDefault = pDeviceInfo->isDefault;
    deviceInfo.nativeDataFormatCount = pDeviceInfo->nativeDataFormatCount;

    if (deviceInfo.nativeDataFormatCount > 0) {
        deviceInfo.nativeDataFormats = static_cast<native_data_format*>(
            ma_malloc(sizeof(native_data_format) * deviceInfo.nativeDataFormatCount, nullptr));

        if (deviceInfo.nativeDataFormats != nullptr) {
            for (ma_uint32 i = 0; i < deviceInfo.nativeDataFormatCount; ++i) {
                deviceInfo.nativeDataFormats[i].format = pDeviceInfo->nativeDataFormats[i].format;
                deviceInfo.nativeDataFormats[i].channels = pDeviceInfo->nativeDataFormats[i].channels;
                deviceInfo.nativeDataFormats[i].sampleRate = pDeviceInfo->nativeDataFormats[i].sampleRate;
                deviceInfo.nativeDataFormats[i].flags = pDeviceInfo->nativeDataFormats[i].flags;
            }
        } else {
            // Memory allocation failed, reset count
            deviceInfo.nativeDataFormatCount = 0;
        }
    } else {
        deviceInfo.nativeDataFormats = nullptr;
    }

    return deviceInfo;
}

extern "C" {

// Frees a structure allocated with sf_create().
MA_API void sf_free(void *ptr) {
    ma_free(ptr, nullptr);
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
MA_API ma_device_config* sf_allocate_device_config(const ma_device_type deviceType, const ma_uint32 sampleRate, const ma_device_data_proc onData, const sf_DeviceConfig* pSfConfig) {
    auto config = sf_create(ma_device_config);
    if (config == nullptr) {
        return nullptr;
    }

    // Initialize with miniaudio defaults
    *config = ma_device_config_init(deviceType);

    // Basic setup from non-DTO parameters
    config->dataCallback = onData;
    config->pUserData = nullptr;
    config->sampleRate = sampleRate;

    // Apply settings from the config DTO if it's provided.
    if (pSfConfig != nullptr) {
        // General settings
        config->periodSizeInFrames = pSfConfig->periodSizeInFrames;
        config->periodSizeInMilliseconds = pSfConfig->periodSizeInMilliseconds;
        config->periods = pSfConfig->periods;
        config->noPreSilencedOutputBuffer = pSfConfig->noPreSilencedOutputBuffer;
        config->noClip = pSfConfig->noClip;
        config->noDisableDenormals = pSfConfig->noDisableDenormals;
        config->noFixedSizedCallback = pSfConfig->noFixedSizedCallback;

        // Playback and Capture sub-configs
        if (pSfConfig->playback != nullptr) {
            config->playback.format = pSfConfig->playback->format;
            config->playback.channels = pSfConfig->playback->channels;
            config->playback.pDeviceID = pSfConfig->playback->pDeviceID;
            config->playback.shareMode = pSfConfig->playback->shareMode;
        }
        if (pSfConfig->capture != nullptr) {
            config->capture.format = pSfConfig->capture->format;
            config->capture.channels = pSfConfig->capture->channels;
            config->capture.pDeviceID = pSfConfig->capture->pDeviceID;
            config->capture.shareMode = pSfConfig->capture->shareMode;
        }

        // Backend-specific settings
        if (pSfConfig->wasapi != nullptr) {
            config->wasapi.usage = pSfConfig->wasapi->usage;
            config->wasapi.noAutoConvertSRC = pSfConfig->wasapi->noAutoConvertSRC;
            config->wasapi.noDefaultQualitySRC = pSfConfig->wasapi->noDefaultQualitySRC;
            config->wasapi.noAutoStreamRouting = pSfConfig->wasapi->noAutoStreamRouting;
            config->wasapi.noHardwareOffloading = pSfConfig->wasapi->noHardwareOffloading;
        }
        if (pSfConfig->coreaudio != nullptr) {
            config->coreaudio.allowNominalSampleRateChange = pSfConfig->coreaudio->allowNominalSampleRateChange;
        }
        if (pSfConfig->alsa != nullptr) {
            config->alsa.noMMap = pSfConfig->alsa->noMMap;
            config->alsa.noAutoFormat = pSfConfig->alsa->noAutoFormat;
            config->alsa.noAutoChannels = pSfConfig->alsa->noAutoChannels;
            config->alsa.noAutoResample = pSfConfig->alsa->noAutoResample;
        }
        if (pSfConfig->pulse != nullptr) {
            config->pulse.pStreamNamePlayback = pSfConfig->pulse->pStreamNamePlayback;
            config->pulse.pStreamNameCapture = pSfConfig->pulse->pStreamNameCapture;
        }
        if (pSfConfig->opensl != nullptr) {
            config->opensl.streamType = pSfConfig->opensl->streamType;
            config->opensl.recordingPreset = pSfConfig->opensl->recordingPreset;
        }
        if (pSfConfig->aaudio != nullptr) {
            config->aaudio.usage = pSfConfig->aaudio->usage;
            config->aaudio.contentType = pSfConfig->aaudio->contentType;
            config->aaudio.inputPreset = pSfConfig->aaudio->inputPreset;
            config->aaudio.allowedCapturePolicy = pSfConfig->aaudio->allowedCapturePolicy;
        }
    } else {
        // Default settings when no config provided
        config->playback.channels = 2;
        config->capture.channels = 2;
        config->playback.shareMode = ma_share_mode_shared;
        config->capture.shareMode = ma_share_mode_shared;
    }

    return config;
}

// Allocate memory for a decoder configuration struct.
MA_API ma_decoder_config *sf_allocate_decoder_config(const ma_format outputFormat, const ma_uint32 outputChannels,
                                                     const ma_uint32 outputSampleRate) {
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
                                                     const ma_uint32 channels, const ma_uint32 sampleRate) {
    auto pConfig = sf_create(ma_encoder_config);
    if (pConfig == nullptr) {
        return nullptr;
    }

    MA_ZERO_OBJECT(pConfig);
    *pConfig = ma_encoder_config_init(encodingFormat, format, channels, sampleRate);

    return pConfig;
}

// Frees memory allocated for an array of device infos.
MA_API void sf_free_device_infos(sf_device_info* deviceInfos, const ma_uint32 count) {
    if (deviceInfos == nullptr) {
        return;
    }

    for (ma_uint32 i = 0; i < count; ++i) {
        if (deviceInfos[i].nativeDataFormats != nullptr) {
            ma_free(deviceInfos[i].nativeDataFormats, nullptr);
        }
    }
    ma_free(deviceInfos, nullptr);
}

// Retrieves a list of available devices.
MA_API ma_result sf_get_devices(ma_context *context, sf_device_info **ppPlaybackDeviceInfos,
                         sf_device_info **ppCaptureDeviceInfos, ma_uint32 *pPlaybackDeviceCount,
                         ma_uint32 *pCaptureDeviceCount) {
    // Validate input parameters
    if (context == nullptr || ppPlaybackDeviceInfos == nullptr || ppCaptureDeviceInfos == nullptr ||
        pPlaybackDeviceCount == nullptr || pCaptureDeviceCount == nullptr) {
        return MA_INVALID_ARGS;
    }

    // Initialize output parameters
    *ppPlaybackDeviceInfos = nullptr;
    *ppCaptureDeviceInfos = nullptr;
    *pPlaybackDeviceCount = 0;
    *pCaptureDeviceCount = 0;

    ma_device_info *pPlaybackDevices = nullptr;
    ma_device_info *pCaptureDevices = nullptr;

    const ma_result result = ma_context_get_devices(context,
                                               &pPlaybackDevices,
                                               pPlaybackDeviceCount,
                                               &pCaptureDevices,
                                               pCaptureDeviceCount);

    if (result != MA_SUCCESS) {
        return result;
    }

    // Handle playback devices
    if (*pPlaybackDeviceCount > 0 && pPlaybackDevices != nullptr) {
        *ppPlaybackDeviceInfos = static_cast<sf_device_info*>(
            ma_malloc(sizeof(sf_device_info) * *pPlaybackDeviceCount, nullptr));

        if (*ppPlaybackDeviceInfos == nullptr) {
            return MA_OUT_OF_MEMORY;
        }

        for (ma_uint32 iDevice = 0; iDevice < *pPlaybackDeviceCount; ++iDevice) {
            (*ppPlaybackDeviceInfos)[iDevice] = sf_create_device_info(&pPlaybackDevices[iDevice]);
        }
    }

    // Handle capture devices
    if (*pCaptureDeviceCount > 0 && pCaptureDevices != nullptr) {
        *ppCaptureDeviceInfos = static_cast<sf_device_info*>(
            ma_malloc(sizeof(sf_device_info) * *pCaptureDeviceCount, nullptr));

        if (*ppCaptureDeviceInfos == nullptr) {
            // Clean up playback devices on failure
            if (*ppPlaybackDeviceInfos != nullptr) {
                sf_free_device_infos(*ppPlaybackDeviceInfos, *pPlaybackDeviceCount);
                *ppPlaybackDeviceInfos = nullptr;
                *pPlaybackDeviceCount = 0;
            }
            return MA_OUT_OF_MEMORY;
        }

        for (ma_uint32 iDevice = 0; iDevice < *pCaptureDeviceCount; ++iDevice) {
            (*ppCaptureDeviceInfos)[iDevice] = sf_create_device_info(&pCaptureDevices[iDevice]);
        }
    }

    return result;
}

}