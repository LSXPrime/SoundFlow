#define MINIAUDIO_IMPLEMENTATION

#include "library.h"
#include <string.h>

// Helper macro for memory allocation
#define sf_create(t) (t*)ma_malloc(sizeof(t), NULL)

// Helper function to safely copy a UTF-8 string
static void sf_safe_strcpy(char* dest, const char* src) {
    if (dest == NULL) {
        return;
    }

    const size_t dest_size = MA_MAX_DEVICE_NAME_LENGTH + 1;

    // Copy the UTF-8 string. Miniaudio provides names in UTF-8.
    strncpy(dest, src, dest_size);

    // Ensure the destination is null-terminated for safety.
    dest[dest_size - 1] = '\0';
}

// Helper function to create device info structure - eliminates code duplication
static struct sf_device_info sf_create_device_info(const ma_device_info* pBasicInfo, const ma_device_info* pFullInfo) {
    struct sf_device_info deviceInfo;

    if (pBasicInfo == NULL || pFullInfo == NULL) {
        memset(&deviceInfo, 0, sizeof(deviceInfo));
        return deviceInfo;
    }

    deviceInfo.id = (ma_device_id*)(&pBasicInfo->id);

    // Use the rest of the information from the full info struct.
    sf_safe_strcpy(deviceInfo.name, pFullInfo->name);
    deviceInfo.isDefault = pFullInfo->isDefault;
    deviceInfo.nativeDataFormatCount = pFullInfo->nativeDataFormatCount;

    if (deviceInfo.nativeDataFormatCount > 0) {
        deviceInfo.nativeDataFormats = (struct native_data_format*)(
            ma_malloc(sizeof(struct native_data_format) * deviceInfo.nativeDataFormatCount, NULL));

        if (deviceInfo.nativeDataFormats != NULL) {
            for (ma_uint32 i = 0; i < deviceInfo.nativeDataFormatCount; ++i) {
                deviceInfo.nativeDataFormats[i].format = pFullInfo->nativeDataFormats[i].format;
                deviceInfo.nativeDataFormats[i].channels = pFullInfo->nativeDataFormats[i].channels;
                deviceInfo.nativeDataFormats[i].sampleRate = pFullInfo->nativeDataFormats[i].sampleRate;
                deviceInfo.nativeDataFormats[i].flags = pFullInfo->nativeDataFormats[i].flags;
            }
        } else {
            // Memory allocation failed, reset count
            deviceInfo.nativeDataFormatCount = 0;
        }
    } else {
        deviceInfo.nativeDataFormats = NULL;
    }

    return deviceInfo;
}

// Frees a structure allocated with sf_create().
MA_API void sf_free(void *ptr) {
    ma_free(ptr, NULL);
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
MA_API ma_device_config* sf_allocate_device_config(const ma_device_type deviceType, const ma_uint32 sampleRate, const ma_device_data_proc onData, const struct sf_DeviceConfig* pSfConfig) {
    ma_device_config* config = sf_create(ma_device_config);
    if (config == NULL) {
        return NULL;
    }

    // Initialize with miniaudio defaults
    *config = ma_device_config_init(deviceType);

    // Basic setup from non-DTO parameters
    config->dataCallback = onData;
    config->pUserData = NULL;
    config->sampleRate = sampleRate;

    // Apply settings from the config DTO if it's provided.
    if (pSfConfig != NULL) {
        // General settings
        config->periodSizeInFrames = pSfConfig->periodSizeInFrames;
        config->periodSizeInMilliseconds = pSfConfig->periodSizeInMilliseconds;
        config->periods = pSfConfig->periods;
        config->noPreSilencedOutputBuffer = pSfConfig->noPreSilencedOutputBuffer;
        config->noClip = pSfConfig->noClip;
        config->noDisableDenormals = pSfConfig->noDisableDenormals;
        config->noFixedSizedCallback = pSfConfig->noFixedSizedCallback;

        // Playback and Capture sub-configs
        if (pSfConfig->playback != NULL) {
            config->playback.format = pSfConfig->playback->format;
            config->playback.channels = pSfConfig->playback->channels;
            config->playback.pDeviceID = pSfConfig->playback->pDeviceID;
            config->playback.shareMode = pSfConfig->playback->shareMode;
        }
        if (pSfConfig->capture != NULL) {
            config->capture.format = pSfConfig->capture->format;
            config->capture.channels = pSfConfig->capture->channels;
            config->capture.pDeviceID = pSfConfig->capture->pDeviceID;
            config->capture.shareMode = pSfConfig->capture->shareMode;
        }

        // Backend-specific settings
        if (pSfConfig->wasapi != NULL) {
            config->wasapi.usage = pSfConfig->wasapi->usage;
            config->wasapi.noAutoConvertSRC = pSfConfig->wasapi->noAutoConvertSRC;
            config->wasapi.noDefaultQualitySRC = pSfConfig->wasapi->noDefaultQualitySRC;
            config->wasapi.noAutoStreamRouting = pSfConfig->wasapi->noAutoStreamRouting;
            config->wasapi.noHardwareOffloading = pSfConfig->wasapi->noHardwareOffloading;
        }
        if (pSfConfig->coreaudio != NULL) {
            config->coreaudio.allowNominalSampleRateChange = pSfConfig->coreaudio->allowNominalSampleRateChange;
        }
        if (pSfConfig->alsa != NULL) {
            config->alsa.noMMap = pSfConfig->alsa->noMMap;
            config->alsa.noAutoFormat = pSfConfig->alsa->noAutoFormat;
            config->alsa.noAutoChannels = pSfConfig->alsa->noAutoChannels;
            config->alsa.noAutoResample = pSfConfig->alsa->noAutoResample;
        }
        if (pSfConfig->pulse != NULL) {
            config->pulse.pStreamNamePlayback = pSfConfig->pulse->pStreamNamePlayback;
            config->pulse.pStreamNameCapture = pSfConfig->pulse->pStreamNameCapture;
        }
        if (pSfConfig->opensl != NULL) {
            config->opensl.streamType = pSfConfig->opensl->streamType;
            config->opensl.recordingPreset = pSfConfig->opensl->recordingPreset;
        }
        if (pSfConfig->aaudio != NULL) {
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
    ma_decoder_config *pConfig = sf_create(ma_decoder_config);
    if (pConfig == NULL) {
        return NULL;
    }

    MA_ZERO_OBJECT(pConfig);
    *pConfig = ma_decoder_config_init(outputFormat, outputChannels, outputSampleRate);

    return pConfig;
}

// Allocate memory for an encoder configuration struct.
MA_API ma_encoder_config *sf_allocate_encoder_config(const ma_format format,
                                                     const ma_uint32 channels, const ma_uint32 sampleRate) {
    ma_encoder_config *pConfig = sf_create(ma_encoder_config);
    if (pConfig == NULL) {
        return NULL;
    }

    MA_ZERO_OBJECT(pConfig);
    *pConfig = ma_encoder_config_init(ma_encoding_format_wav, format, channels, sampleRate);

    return pConfig;
}

// Frees memory allocated for an array of device infos.
MA_API void sf_free_device_infos(struct sf_device_info* deviceInfos, const ma_uint32 count) {
    if (deviceInfos == NULL) {
        return;
    }

    for (ma_uint32 i = 0; i < count; ++i) {
        if (deviceInfos[i].nativeDataFormats != NULL) {
            ma_free(deviceInfos[i].nativeDataFormats, NULL);
        }
    }
    ma_free(deviceInfos, NULL);
}

// Retrieves a list of available devices.
MA_API ma_result sf_get_devices(ma_context *context, struct sf_device_info **ppPlaybackDeviceInfos,
                         struct sf_device_info **ppCaptureDeviceInfos, ma_uint32 *pPlaybackDeviceCount,
                         ma_uint32 *pCaptureDeviceCount) {
    // Validate input parameters
    if (context == NULL || ppPlaybackDeviceInfos == NULL || ppCaptureDeviceInfos == NULL ||
        pPlaybackDeviceCount == NULL || pCaptureDeviceCount == NULL) {
        return MA_INVALID_ARGS;
    }

    // Initialize output parameters
    *ppPlaybackDeviceInfos = NULL;
    *ppCaptureDeviceInfos = NULL;
    *pPlaybackDeviceCount = 0;
    *pCaptureDeviceCount = 0;

    ma_device_info *pEnumeratedPlaybackDevices = NULL;
    ma_device_info *pEnumeratedCaptureDevices = NULL;

    // Enumerate devices to get their IDs and names.
    const ma_result result = ma_context_get_devices(context,
                                               &pEnumeratedPlaybackDevices,
                                               pPlaybackDeviceCount,
                                               &pEnumeratedCaptureDevices,
                                               pCaptureDeviceCount);

    if (result != MA_SUCCESS) {
        return result;
    }

    // Handle playback devices
    if (*pPlaybackDeviceCount > 0 && pEnumeratedPlaybackDevices != NULL) {
        *ppPlaybackDeviceInfos = (struct sf_device_info*)(
            ma_malloc(sizeof(struct sf_device_info) * *pPlaybackDeviceCount, NULL));

        if (*ppPlaybackDeviceInfos == NULL) {
            return MA_OUT_OF_MEMORY;
        }

        for (ma_uint32 iDevice = 0; iDevice < *pPlaybackDeviceCount; ++iDevice) {
            // Get detailed info for each specific device.
            ma_device_info fullDeviceInfo;
            ma_result infoResult = ma_context_get_device_info(context, ma_device_type_playback, &pEnumeratedPlaybackDevices[iDevice].id, &fullDeviceInfo);

            // Use the full info if successful, otherwise fall back to the basic enumerated info.
            const ma_device_info* pSourceInfo = infoResult == MA_SUCCESS ? &fullDeviceInfo : &pEnumeratedPlaybackDevices[iDevice];

            (*ppPlaybackDeviceInfos)[iDevice] = sf_create_device_info(&pEnumeratedPlaybackDevices[iDevice], pSourceInfo);
        }
    }

    // Handle capture devices
    if (*pCaptureDeviceCount > 0 && pEnumeratedCaptureDevices != NULL) {
        *ppCaptureDeviceInfos = (struct sf_device_info*)(
            ma_malloc(sizeof(struct sf_device_info) * *pCaptureDeviceCount, NULL));

        if (*ppCaptureDeviceInfos == NULL) {
            // Clean up playback devices on failure
            if (*ppPlaybackDeviceInfos != NULL) {
                sf_free_device_infos(*ppPlaybackDeviceInfos, *pPlaybackDeviceCount);
                *ppPlaybackDeviceInfos = NULL;
                *pPlaybackDeviceCount = 0;
            }
            return MA_OUT_OF_MEMORY;
        }

        for (ma_uint32 iDevice = 0; iDevice < *pCaptureDeviceCount; ++iDevice) {
            // Get detailed info for each specific device.
            ma_device_info fullDeviceInfo;
            ma_result infoResult = ma_context_get_device_info(context, ma_device_type_capture, &pEnumeratedCaptureDevices[iDevice].id, &fullDeviceInfo);

            // Use the full info if successful, otherwise fall back to the basic enumerated info.
            const ma_device_info* pSourceInfo = infoResult == MA_SUCCESS ? &fullDeviceInfo : &pEnumeratedCaptureDevices[iDevice];

            (*ppCaptureDeviceInfos)[iDevice] = sf_create_device_info(&pEnumeratedCaptureDevices[iDevice], pSourceInfo);
        }
    }

    return result;
}

// Returns the backend used by the context.
MA_API ma_backend sf_context_get_backend(const ma_context* pContext)
{
    if (pContext == NULL) {
        return ma_backend_null;
    }

    return pContext->backend;
}