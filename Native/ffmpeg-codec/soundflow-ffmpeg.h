#ifndef SOUNDFLOW-FFMPEG_H
#define SOUNDFLOW-FFMPEG_H

#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
#define SF_FFMPEG_API __declspec(dllexport)
#else
#define SF_FFMPEG_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// Opaque handles
typedef struct SF_Decoder SF_Decoder;
typedef struct SF_Encoder SF_Encoder;

typedef enum {
  SF_SAMPLE_FORMAT_UNKNOWN = 0,
  SF_SAMPLE_FORMAT_U8 = 1,
  SF_SAMPLE_FORMAT_S16 = 2,
  SF_SAMPLE_FORMAT_S24 = 3,
  SF_SAMPLE_FORMAT_S32 = 4,
  SF_SAMPLE_FORMAT_F32 = 5,
} SFSampleFormat;

typedef enum {
    SF_RESULT_SUCCESS = 0,

    // General Errors
    SF_RESULT_ERROR_INVALID_ARGS = -1,
    SF_RESULT_ERROR_ALLOCATION_FAILED = -2,

    // Decoder-specific Errors
    SF_RESULT_DECODER_ERROR_OPEN_INPUT = -10,
    SF_RESULT_DECODER_ERROR_FIND_STREAM_INFO = -11,
    SF_RESULT_DECODER_ERROR_NO_AUDIO_STREAM = -12,
    SF_RESULT_DECODER_ERROR_CODEC_NOT_FOUND = -13,
    SF_RESULT_DECODER_ERROR_CODEC_CONTEXT_ALLOC = -14,
    SF_RESULT_DECODER_ERROR_CODEC_OPEN_FAILED = -15,
    SF_RESULT_DECODER_ERROR_INVALID_TARGET_FORMAT = -16,
    SF_RESULT_DECODER_ERROR_RESAMPLER_INIT_FAILED = -17,
    SF_RESULT_DECODER_ERROR_PACKET_FRAME_ALLOC = -18,
    SF_RESULT_DECODER_ERROR_SEEK_FAILED = -19,
    SF_RESULT_DECODER_ERROR_DECODING_FAILED = -20,

    // Encoder-specific Errors
    SF_RESULT_ENCODER_ERROR_FORMAT_NOT_FOUND = -30,
    SF_RESULT_ENCODER_ERROR_CODEC_NOT_FOUND = -31,
    SF_RESULT_ENCODER_ERROR_STREAM_ALLOC = -32,
    SF_RESULT_ENCODER_ERROR_CODEC_CONTEXT_ALLOC = -33,
    SF_RESULT_ENCODER_ERROR_CODEC_OPEN_FAILED = -34,
    SF_RESULT_ENCODER_ERROR_CONTEXT_PARAMS = -35,
    SF_RESULT_ENCODER_ERROR_WRITE_HEADER = -36,
    SF_RESULT_ENCODER_ERROR_INVALID_INPUT_FORMAT = -37,
    SF_RESULT_ENCODER_ERROR_RESAMPLER_INIT_FAILED = -38,
    SF_RESULT_ENCODER_ERROR_PACKET_FRAME_ALLOC = -39,
    SF_RESULT_ENCODER_ERROR_ENCODING_FAILED = -40,
    SF_RESULT_ENCODER_ERROR_WRITE_FAILED = -41

} SF_Result;

// Callbacks for custom I/O
typedef size_t (*sf_read_callback)(void* pUserData, void* pBuffer,
                                   size_t bytesToRead);
typedef int64_t (*sf_seek_callback)(void* pUserData, int64_t offset,
                                    int whence);
typedef size_t (*sf_write_callback)(void* pUserData, void* pBuffer,
                                    size_t bytesToWrite);

// Decoder Functions
SF_FFMPEG_API SF_Decoder* sf_decoder_create();
SF_FFMPEG_API SF_Result sf_decoder_init(
    SF_Decoder* decoder, sf_read_callback onRead, sf_seek_callback onSeek,
    void* pUserData,
    SFSampleFormat target_format,       // The target output format
    SFSampleFormat* out_native_format,  // The original format of the file
    uint32_t* out_channels, uint32_t* out_samplerate);
SF_FFMPEG_API int64_t sf_decoder_get_length_in_pcm_frames(SF_Decoder* decoder);
SF_FFMPEG_API SF_Result sf_decoder_read_pcm_frames(SF_Decoder* decoder,
                                                   void* pFramesOut,
                                                   int64_t frameCount,
                                                   int64_t* out_frames_read);
SF_FFMPEG_API SF_Result sf_decoder_seek_to_pcm_frame(SF_Decoder* decoder,
                                                     int64_t frameIndex);
SF_FFMPEG_API void sf_decoder_free(SF_Decoder* decoder);

// Encoder Functions
SF_FFMPEG_API SF_Encoder* sf_encoder_create();
// format_name is e.g. "mp3", "flac", "wav", "opus"
SF_FFMPEG_API SF_Result sf_encoder_init(SF_Encoder* encoder, const char* format_name,
                                        sf_write_callback onWrite, void* pUserData,
                                        SFSampleFormat sampleFormat,
                                        uint32_t channels, uint32_t sampleRate);
SF_FFMPEG_API SF_Result sf_encoder_write_pcm_frames(SF_Encoder* encoder,
                                                    void* pFramesIn,
                                                    int64_t frameCount,
                                                    int64_t* out_frames_written);
SF_FFMPEG_API void sf_encoder_free(SF_Encoder* encoder);

// Helper Functions
SF_FFMPEG_API const char* sf_result_to_string(SF_Result result);

#ifdef __cplusplus
}
#endif

#endif  // SOUNDFLOW-FFMPEG_H