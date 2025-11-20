#include "soundflow-ffmpeg.h"

#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/opt.h>
#include <libavutil/channel_layout.h>
#include <libswresample/swresample.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define IO_BUFFER_SIZE 32768

// Internal Structs

struct SF_Decoder {
    AVFormatContext* format_ctx;
    AVCodecContext* codec_ctx;
    AVPacket* packet;
    AVFrame* frame;
    SwrContext* swr_ctx;
    int stream_index;
    AVIOContext* avio_ctx;
    uint8_t* io_buffer;
    sf_read_callback onRead;
    sf_seek_callback onSeek;
    void* pUserData;
    int target_bytes_per_sample;
    int target_channels;
};

struct SF_Encoder {
    AVFormatContext* format_ctx;
    AVCodecContext* codec_ctx;
    AVStream* stream;
    AVPacket* packet;
    AVFrame* frame;
    SwrContext* swr_ctx;
    AVIOContext* avio_ctx;
    uint8_t* io_buffer;
    sf_write_callback onWrite;
    void* pUserData;
    int64_t next_pts;
    SFSampleFormat input_format;
};

// Helper Functions

static enum AVSampleFormat to_ffmpeg_sample_format(SFSampleFormat format) {
    switch (format) {
        case SF_SAMPLE_FORMAT_U8:  return AV_SAMPLE_FMT_U8;
        case SF_SAMPLE_FORMAT_S16: return AV_SAMPLE_FMT_S16;
        // FFmpeg uses 32-bit containers for 24-bit audio, which is fine.
        case SF_SAMPLE_FORMAT_S24: return AV_SAMPLE_FMT_S32;
        case SF_SAMPLE_FORMAT_S32: return AV_SAMPLE_FMT_S32;
        case SF_SAMPLE_FORMAT_F32: return AV_SAMPLE_FMT_FLT;
        default: return AV_SAMPLE_FMT_NONE;
    }
}

static SFSampleFormat from_ffmpeg_sample_format(enum AVSampleFormat format) {
    switch (format) {
        case AV_SAMPLE_FMT_U8:
        case AV_SAMPLE_FMT_U8P:  return SF_SAMPLE_FORMAT_U8;
        case AV_SAMPLE_FMT_S16:
        case AV_SAMPLE_FMT_S16P: return SF_SAMPLE_FORMAT_S16;
        case AV_SAMPLE_FMT_S32:
        case AV_SAMPLE_FMT_S32P: return SF_SAMPLE_FORMAT_S32;
        case AV_SAMPLE_FMT_FLT:
        case AV_SAMPLE_FMT_FLTP: return SF_SAMPLE_FORMAT_F32;
        // FFmpeg does not support a native packed 24-bit format.
        default: return SF_SAMPLE_FORMAT_UNKNOWN;
    }
}

// I/O Callbacks

static int read_packet_callback(void* opaque, uint8_t* buf, int buf_size) {
    SF_Decoder* decoder = (SF_Decoder*)opaque;
    size_t bytes_read = decoder->onRead(decoder->pUserData, buf, buf_size);
    if (bytes_read == 0) {
        return AVERROR_EOF;
    }
    return (int)bytes_read;
}

static int64_t seek_callback_wrapper(void* opaque, int64_t offset, int whence) {
    SF_Decoder* decoder = (SF_Decoder*)opaque;
    // AVSEEK_SIZE is a special request to get the file size.
    if (whence == AVSEEK_SIZE) {
        return -1;
    }
    return decoder->onSeek(decoder->pUserData, offset, whence);
}

static int write_packet_callback(void* opaque, const uint8_t* buf, int buf_size) {
    SF_Encoder* encoder = (SF_Encoder*)opaque;
    size_t bytes_written = encoder->onWrite(encoder->pUserData, (void*)buf, buf_size);
    if (bytes_written < buf_size) {
        // Signal an I/O error to FFmpeg if the write was incomplete.
        return AVERROR(EIO);
    }
    return (int)bytes_written;
}


//  Decoder Implementation

SF_FFMPEG_API SF_Decoder* sf_decoder_create() {
    return (SF_Decoder*)calloc(1, sizeof(SF_Decoder));
}

SF_FFMPEG_API SF_Result sf_decoder_init(SF_Decoder* decoder, sf_read_callback onRead, sf_seek_callback onSeek, void* pUserData,
                                        SFSampleFormat target_format, SFSampleFormat* out_native_format,
                                        uint32_t* out_channels, uint32_t* out_samplerate) {
    if (!decoder) return SF_RESULT_ERROR_INVALID_ARGS;

    // Set FFmpeg to only log errors
    av_log_set_level(AV_LOG_ERROR);

    decoder->onRead = onRead;
    decoder->onSeek = onSeek;
    decoder->pUserData = pUserData;

    decoder->format_ctx = avformat_alloc_context();
    if (!decoder->format_ctx) return SF_RESULT_ERROR_ALLOCATION_FAILED;
    decoder->io_buffer = (uint8_t*)av_malloc(IO_BUFFER_SIZE);
    if (!decoder->io_buffer) { avformat_free_context(decoder->format_ctx); return SF_RESULT_ERROR_ALLOCATION_FAILED; }

    decoder->avio_ctx = avio_alloc_context(decoder->io_buffer, IO_BUFFER_SIZE, 0, decoder, read_packet_callback, NULL, seek_callback_wrapper);
    if (!decoder->avio_ctx) { av_free(decoder->io_buffer); avformat_free_context(decoder->format_ctx); return SF_RESULT_ERROR_ALLOCATION_FAILED; }
    decoder->format_ctx->pb = decoder->avio_ctx;

    // Add format context options to ignore non-audio streams
    AVDictionary* options = NULL;
    av_dict_set(&options, "scan_all_pmts", "0", 0);
    av_dict_set(&options, "probesize", "5000000", 0);
    av_dict_set(&options, "analyzeduration", "10000000", 0);

    if (avformat_open_input(&decoder->format_ctx, NULL, NULL, &options) != 0) return SF_RESULT_DECODER_ERROR_OPEN_INPUT;
    if (avformat_find_stream_info(decoder->format_ctx, NULL) < 0) return SF_RESULT_DECODER_ERROR_FIND_STREAM_INFO;

    decoder->stream_index = av_find_best_stream(decoder->format_ctx, AVMEDIA_TYPE_AUDIO, -1, -1, NULL, 0);
    if (decoder->stream_index < 0) return SF_RESULT_DECODER_ERROR_NO_AUDIO_STREAM;

    // Ignore all non-audio streams
    for (unsigned int i = 0; i < decoder->format_ctx->nb_streams; i++) {
        if (i != decoder->stream_index) {
            decoder->format_ctx->streams[i]->discard = AVDISCARD_ALL;
        }
    }

    AVStream* stream = decoder->format_ctx->streams[decoder->stream_index];
    const AVCodec* codec = avcodec_find_decoder(stream->codecpar->codec_id);
    if (!codec) return SF_RESULT_DECODER_ERROR_CODEC_NOT_FOUND;

    decoder->codec_ctx = avcodec_alloc_context3(codec);
    if (!decoder->codec_ctx) return SF_RESULT_DECODER_ERROR_CODEC_CONTEXT_ALLOC;

    avcodec_parameters_to_context(decoder->codec_ctx, stream->codecpar);
    if (avcodec_open2(decoder->codec_ctx, codec, NULL) < 0) return SF_RESULT_DECODER_ERROR_CODEC_OPEN_FAILED;

    *out_channels = decoder->codec_ctx->ch_layout.nb_channels;
    *out_samplerate = decoder->codec_ctx->sample_rate;
    *out_native_format = from_ffmpeg_sample_format(decoder->codec_ctx->sample_fmt);

    enum AVSampleFormat target_av_format = to_ffmpeg_sample_format(target_format);
    if (target_av_format == AV_SAMPLE_FMT_NONE) return SF_RESULT_DECODER_ERROR_INVALID_TARGET_FORMAT;

    decoder->target_bytes_per_sample = av_get_bytes_per_sample(target_av_format);
    decoder->target_channels = decoder->codec_ctx->ch_layout.nb_channels;

    // Setup resampler to convert from native format to the requested target format
    swr_alloc_set_opts2(&decoder->swr_ctx,
                        &decoder->codec_ctx->ch_layout, target_av_format, decoder->codec_ctx->sample_rate,
                        &decoder->codec_ctx->ch_layout, decoder->codec_ctx->sample_fmt, decoder->codec_ctx->sample_rate,
                        0, NULL);
    if (!decoder->swr_ctx || swr_init(decoder->swr_ctx) < 0) return SF_RESULT_DECODER_ERROR_RESAMPLER_INIT_FAILED;

    decoder->packet = av_packet_alloc();
    decoder->frame = av_frame_alloc();
    if (!decoder->packet || !decoder->frame) return SF_RESULT_DECODER_ERROR_PACKET_FRAME_ALLOC;

    return SF_RESULT_SUCCESS;
}

SF_FFMPEG_API int64_t sf_decoder_get_length_in_pcm_frames(SF_Decoder* decoder) {
    if (!decoder || !decoder->format_ctx || decoder->stream_index < 0) return -1;
    AVStream* stream = decoder->format_ctx->streams[decoder->stream_index];
    if (stream->duration != AV_NOPTS_VALUE) {
        return av_rescale_q(stream->duration, stream->time_base, (AVRational){1, stream->codecpar->sample_rate});
    }
    // Fallback for formats without duration info (e.g. WAV)
    if (decoder->format_ctx->duration != AV_NOPTS_VALUE) {
        return av_rescale(decoder->format_ctx->duration, AV_TIME_BASE, stream->codecpar->sample_rate);
    }
    return 0;
}

SF_FFMPEG_API SF_Result sf_decoder_read_pcm_frames(SF_Decoder* decoder, void* pFramesOut, int64_t frameCount, int64_t* out_frames_read) {
    if (!decoder || !pFramesOut || !out_frames_read || frameCount <= 0) return SF_RESULT_ERROR_INVALID_ARGS;

    *out_frames_read = 0;
    uint8_t* out_ptr[] = { (uint8_t*)pFramesOut };
    int64_t frames_read = 0;
    int draining = 0;

    while (frames_read < frameCount) {
        // Try to receive a decoded frame
        int ret = avcodec_receive_frame(decoder->codec_ctx, decoder->frame);

        if (ret == 0) {
            // Resample the frame to target format
            int out_samples = swr_convert(decoder->swr_ctx,
                                         out_ptr,
                                         (int)(frameCount - frames_read),
                                         (const uint8_t**)decoder->frame->data,
                                         decoder->frame->nb_samples);

            if (out_samples > 0) {
                out_ptr[0] += out_samples * decoder->target_channels * decoder->target_bytes_per_sample;
                frames_read += out_samples;
            }
            av_frame_unref(decoder->frame);
            continue;
        }
        else if (ret == AVERROR_EOF) {
            // Drain the resampler completely
            int flushed_samples;
            do {
                flushed_samples = swr_convert(decoder->swr_ctx,
                                             out_ptr,
                                             (int)(frameCount - frames_read),
                                             NULL, 0);
                if (flushed_samples > 0) {
                    out_ptr[0] += flushed_samples * decoder->target_channels * decoder->target_bytes_per_sample;
                    frames_read += flushed_samples;
                }
            } while (flushed_samples > 0 && frames_read < frameCount);
            
            // End of stream is not an error, break loop and return success.
            break;
        }
        else if (ret != AVERROR(EAGAIN)) {
            // A unrecoverable decoding error occurred.
            *out_frames_read = frames_read;
            return SF_RESULT_DECODER_ERROR_DECODING_FAILED;
        }

        // If we need more input data (ret == AVERROR(EAGAIN))
        if (draining) {
            // We are draining and still need more data, which means we are done.
            break;
        }

        // Read a new packet from the stream
        av_packet_unref(decoder->packet);
        int read_ret = av_read_frame(decoder->format_ctx, decoder->packet);

        if (read_ret == 0) {
            if (decoder->packet->stream_index == decoder->stream_index) {
                if (avcodec_send_packet(decoder->codec_ctx, decoder->packet) < 0) {
                    av_packet_unref(decoder->packet);
                    *out_frames_read = frames_read;
                    return SF_RESULT_DECODER_ERROR_DECODING_FAILED;
                }
            }
            av_packet_unref(decoder->packet);
        }
        else if (read_ret == AVERROR_EOF) {
            // Reached end of stream, start draining process by sending a NULL packet.
            avcodec_send_packet(decoder->codec_ctx, NULL);
            draining = 1;
        }
        else {
            // A read error occurred.
            *out_frames_read = frames_read;
            return SF_RESULT_DECODER_ERROR_DECODING_FAILED;
        }
    }

    *out_frames_read = frames_read;
    return SF_RESULT_SUCCESS;
}

SF_FFMPEG_API SF_Result sf_decoder_seek_to_pcm_frame(SF_Decoder* decoder, int64_t frameIndex) {
    if (!decoder || !decoder->format_ctx || decoder->stream_index < 0) return SF_RESULT_ERROR_INVALID_ARGS;

    AVStream* stream = decoder->format_ctx->streams[decoder->stream_index];
    int64_t timestamp = av_rescale_q(frameIndex, (AVRational){1, stream->codecpar->sample_rate}, stream->time_base);

    // Flush buffers and seek
    avcodec_flush_buffers(decoder->codec_ctx);
    swr_init(decoder->swr_ctx);  // Reset resampler state

    int ret = av_seek_frame(decoder->format_ctx, decoder->stream_index, timestamp, AVSEEK_FLAG_BACKWARD);
    if (ret < 0) {
        return SF_RESULT_DECODER_ERROR_SEEK_FAILED;
    }

    // Read and discard packets until we reach the desired position
    AVPacket* pkt = av_packet_alloc();
    while (av_read_frame(decoder->format_ctx, pkt) >= 0) {
        if (pkt->stream_index == decoder->stream_index) {
            av_packet_unref(pkt);
            break;
        }
        av_packet_unref(pkt);
    }
    av_packet_free(&pkt);

    return SF_RESULT_SUCCESS;
}

SF_FFMPEG_API void sf_decoder_free(SF_Decoder* decoder) {
    if (!decoder) return;
    avcodec_free_context(&decoder->codec_ctx);

    // Custom IO context needs special handling for freeing
    if (decoder->format_ctx) {
        if (decoder->format_ctx->pb) {
            av_freep(&decoder->format_ctx->pb->buffer);
            avio_context_free(&decoder->format_ctx->pb);
        }
        avformat_close_input(&decoder->format_ctx);
    }

    av_packet_free(&decoder->packet);
    av_frame_free(&decoder->frame);
    swr_free(&decoder->swr_ctx);
    free(decoder);
}


// Encoder Implementation

SF_FFMPEG_API SF_Encoder* sf_encoder_create() {
    return (SF_Encoder*)calloc(1, sizeof(SF_Encoder));
}

static int encode_and_write(SF_Encoder* encoder, AVFrame* frame) {
    int ret = avcodec_send_frame(encoder->codec_ctx, frame);
    if (ret < 0) return ret;

    while (ret >= 0) {
        ret = avcodec_receive_packet(encoder->codec_ctx, encoder->packet);
        if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) {
            return 0;
        } else if (ret < 0) {
            return ret; // Encoding error
        }
        av_packet_rescale_ts(encoder->packet, encoder->codec_ctx->time_base, encoder->stream->time_base);
        encoder->packet->stream_index = encoder->stream->index;

        // Propagate I/O errors from the write callback.
        int write_ret = av_interleaved_write_frame(encoder->format_ctx, encoder->packet);
        av_packet_unref(encoder->packet);
        if (write_ret < 0) {
            return write_ret; // I/O or muxing error
        }
    }
    return 0;
}

SF_FFMPEG_API SF_Result sf_encoder_init(SF_Encoder* encoder, const char* format_name, sf_write_callback onWrite, void* pUserData, SFSampleFormat sampleFormat, uint32_t channels, uint32_t sampleRate) {
    if (!encoder) return SF_RESULT_ERROR_INVALID_ARGS;

    // Set FFmpeg to only log errors
    av_log_set_level(AV_LOG_ERROR);

    encoder->onWrite = onWrite;
    encoder->pUserData = pUserData;
    encoder->next_pts = 0;
    encoder->input_format = sampleFormat;

    const AVOutputFormat* out_fmt = av_guess_format(format_name, NULL, NULL);
    if (!out_fmt) return SF_RESULT_ENCODER_ERROR_FORMAT_NOT_FOUND;

    avformat_alloc_output_context2(&encoder->format_ctx, out_fmt, NULL, NULL);
    if (!encoder->format_ctx) return SF_RESULT_ENCODER_ERROR_FORMAT_NOT_FOUND;

    const AVCodec* codec = avcodec_find_encoder(out_fmt->audio_codec);
    if (!codec) return SF_RESULT_ENCODER_ERROR_CODEC_NOT_FOUND;

    encoder->stream = avformat_new_stream(encoder->format_ctx, codec);
    if (!encoder->stream) return SF_RESULT_ENCODER_ERROR_STREAM_ALLOC;
    encoder->codec_ctx = avcodec_alloc_context3(codec);
    if (!encoder->codec_ctx) return SF_RESULT_ENCODER_ERROR_CODEC_CONTEXT_ALLOC;

    AVChannelLayout ch_layout;
    av_channel_layout_default(&ch_layout, channels);
    av_channel_layout_copy(&encoder->codec_ctx->ch_layout, &ch_layout);
    encoder->codec_ctx->sample_rate = sampleRate;
    encoder->codec_ctx->time_base = (AVRational){1, sampleRate};

    // Suppress deprecation warnings for sample_fmts for compatibility.
    #pragma GCC diagnostic push
    #pragma GCC diagnostic ignored "-Wdeprecated-declarations"

    // Choose the best sample format the encoder supports
    if (codec->sample_fmts) {
        encoder->codec_ctx->sample_fmt = codec->sample_fmts[0];
    } else {
        #pragma GCC diagnostic pop
        return SF_RESULT_ENCODER_ERROR_CODEC_NOT_FOUND;
    }
    #pragma GCC diagnostic pop

    if (encoder->format_ctx->oformat->flags & AVFMT_GLOBALHEADER)
        encoder->codec_ctx->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;

    if (avcodec_open2(encoder->codec_ctx, codec, NULL) < 0) return SF_RESULT_ENCODER_ERROR_CODEC_OPEN_FAILED;
    if (avcodec_parameters_from_context(encoder->stream->codecpar, encoder->codec_ctx) < 0) return SF_RESULT_ENCODER_ERROR_CONTEXT_PARAMS;

    encoder->io_buffer = (uint8_t*)av_malloc(IO_BUFFER_SIZE);
    encoder->avio_ctx = avio_alloc_context(encoder->io_buffer, IO_BUFFER_SIZE, 1, encoder, NULL, write_packet_callback, NULL);
    encoder->format_ctx->pb = encoder->avio_ctx;

    if (avformat_write_header(encoder->format_ctx, NULL) < 0) return SF_RESULT_ENCODER_ERROR_WRITE_HEADER;

    // Setup resampler to convert from the provided input format to the encoder's required format
    enum AVSampleFormat input_av_format = to_ffmpeg_sample_format(sampleFormat);
    if(input_av_format == AV_SAMPLE_FMT_NONE) return SF_RESULT_ENCODER_ERROR_INVALID_INPUT_FORMAT;

    swr_alloc_set_opts2(&encoder->swr_ctx,
                        &encoder->codec_ctx->ch_layout, encoder->codec_ctx->sample_fmt, encoder->codec_ctx->sample_rate,
                        &encoder->codec_ctx->ch_layout, input_av_format, sampleRate, 0, NULL);
    if (!encoder->swr_ctx || swr_init(encoder->swr_ctx) < 0) return SF_RESULT_ENCODER_ERROR_RESAMPLER_INIT_FAILED;

    encoder->packet = av_packet_alloc();
    encoder->frame = av_frame_alloc();
    if (!encoder->packet || !encoder->frame) return SF_RESULT_ENCODER_ERROR_PACKET_FRAME_ALLOC;

    return SF_RESULT_SUCCESS;
}

SF_FFMPEG_API SF_Result sf_encoder_write_pcm_frames(SF_Encoder* encoder, void* pFramesIn, int64_t frameCount, int64_t* out_frames_written) {
    if (!encoder || !pFramesIn || !out_frames_written || frameCount <= 0) return SF_RESULT_ERROR_INVALID_ARGS;

    *out_frames_written = 0;
    AVFrame* resampled_frame = av_frame_alloc();
    resampled_frame->format = encoder->codec_ctx->sample_fmt;
    av_channel_layout_copy(&resampled_frame->ch_layout, &encoder->codec_ctx->ch_layout);
    resampled_frame->sample_rate = encoder->codec_ctx->sample_rate;
    resampled_frame->nb_samples = (int)frameCount;
    if (av_frame_get_buffer(resampled_frame, 0) < 0) {
        av_frame_free(&resampled_frame);
        return SF_RESULT_ERROR_ALLOCATION_FAILED;
    }

    const uint8_t* pIn[] = { (const uint8_t*)pFramesIn };
    swr_convert(encoder->swr_ctx, resampled_frame->data, resampled_frame->nb_samples, pIn, (int)frameCount);

    resampled_frame->pts = encoder->next_pts;
    encoder->next_pts += resampled_frame->nb_samples;
    
    int ret = encode_and_write(encoder, resampled_frame);
    av_frame_free(&resampled_frame);

    if (ret < 0) {
        if (ret == AVERROR(EIO)) {
            return SF_RESULT_ENCODER_ERROR_WRITE_FAILED;
        }
        return SF_RESULT_ENCODER_ERROR_ENCODING_FAILED;
    }

    *out_frames_written = frameCount;
    return SF_RESULT_SUCCESS;
}

SF_FFMPEG_API void sf_encoder_free(SF_Encoder* encoder) {
    if (!encoder) return;

    // Flush the encoder by sending a NULL frame
    encode_and_write(encoder, NULL);

    av_write_trailer(encoder->format_ctx);

    avcodec_free_context(&encoder->codec_ctx);
    if (encoder->format_ctx) {
        if (encoder->format_ctx->pb) {
            // Flush any buffered data before freeing.
            avio_flush(encoder->format_ctx->pb);
            av_freep(&encoder->format_ctx->pb->buffer);
            avio_context_free(&encoder->format_ctx->pb);
        }
        avformat_free_context(encoder->format_ctx);
    }

    av_packet_free(&encoder->packet);
    av_frame_free(&encoder->frame);
    swr_free(&encoder->swr_ctx);

    free(encoder);
}

// Helper Implementation

SF_FFMPEG_API const char* sf_result_to_string(SF_Result result) {
    switch (result) {
        case SF_RESULT_SUCCESS: return "Success";
        case SF_RESULT_ERROR_INVALID_ARGS: return "Invalid arguments provided";
        case SF_RESULT_ERROR_ALLOCATION_FAILED: return "Memory allocation failed";
        case SF_RESULT_DECODER_ERROR_OPEN_INPUT: return "Failed to open input stream";
        case SF_RESULT_DECODER_ERROR_FIND_STREAM_INFO: return "Failed to find stream information";
        case SF_RESULT_DECODER_ERROR_NO_AUDIO_STREAM: return "No suitable audio stream found";
        case SF_RESULT_DECODER_ERROR_CODEC_NOT_FOUND: return "Audio codec not found";
        case SF_RESULT_DECODER_ERROR_CODEC_CONTEXT_ALLOC: return "Failed to allocate codec context";
        case SF_RESULT_DECODER_ERROR_CODEC_OPEN_FAILED: return "Failed to open codec";
        case SF_RESULT_DECODER_ERROR_INVALID_TARGET_FORMAT: return "Invalid target sample format";
        case SF_RESULT_DECODER_ERROR_RESAMPLER_INIT_FAILED: return "Failed to initialize audio resampler";
        case SF_RESULT_DECODER_ERROR_PACKET_FRAME_ALLOC: return "Failed to allocate packet or frame";
        case SF_RESULT_DECODER_ERROR_SEEK_FAILED: return "Seek operation failed";
        case SF_RESULT_DECODER_ERROR_DECODING_FAILED: return "An unrecoverable error occurred during the decoding process";
        case SF_RESULT_ENCODER_ERROR_FORMAT_NOT_FOUND: return "Output format not found";
        case SF_RESULT_ENCODER_ERROR_CODEC_NOT_FOUND: return "Audio codec for the format not found or not enabled";
        case SF_RESULT_ENCODER_ERROR_STREAM_ALLOC: return "Failed to allocate new audio stream";
        case SF_RESULT_ENCODER_ERROR_CODEC_CONTEXT_ALLOC: return "Failed to allocate encoder codec context";
        case SF_RESULT_ENCODER_ERROR_CODEC_OPEN_FAILED: return "Failed to open encoder codec";
        case SF_RESULT_ENCODER_ERROR_CONTEXT_PARAMS: return "Failed to copy codec parameters to stream";
        case SF_RESULT_ENCODER_ERROR_WRITE_HEADER: return "Failed to write output file header";
        case SF_RESULT_ENCODER_ERROR_INVALID_INPUT_FORMAT: return "Invalid input sample format";
        case SF_RESULT_ENCODER_ERROR_RESAMPLER_INIT_FAILED: return "Failed to initialize audio resampler for encoding";
        case SF_RESULT_ENCODER_ERROR_PACKET_FRAME_ALLOC: return "Failed to allocate packet or frame for encoding";
        case SF_RESULT_ENCODER_ERROR_ENCODING_FAILED: return "An unrecoverable error occurred during the encoding process";
        case SF_RESULT_ENCODER_ERROR_WRITE_FAILED: return "An I/O error occurred while writing the encoded data";
        default: return "Unknown error";
    }
}