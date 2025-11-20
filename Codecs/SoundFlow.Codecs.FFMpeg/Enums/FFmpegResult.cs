namespace SoundFlow.Codecs.FFMpeg.Enums;

/// <summary>
/// Mirrors the SF_Result enum from the native soundflow-ffmpeg library,
/// providing strongly-typed result codes for API calls.
/// </summary>
public enum FFmpegResult
{
    /// <summary>
    /// The operation completed successfully.
    /// </summary>
    Success = 0,

    // General Errors
    
    /// <summary>
    /// Invalid arguments (e.g., a null pointer) were provided to a native function.
    /// </summary>
    ErrorInvalidArgs = -1,
    
    /// <summary>
    /// A memory allocation failed within the native library.
    /// </summary>
    ErrorAllocationFailed = -2,

    // Decoder-specific Errors
    
    /// <summary>
    /// Failed to open the input stream. This can happen if the format is not recognized or the data is corrupt.
    /// </summary>
    DecoderErrorOpenInput = -10,
    
    /// <summary>
    /// Could not find or parse stream information from the input.
    /// </summary>
    DecoderErrorFindStreamInfo = -11,
    
    /// <summary>
    /// No suitable audio stream was found in the input file.
    /// </summary>
    DecoderErrorNoAudioStream = -12,
    
    /// <summary>
    /// A decoder for the audio format could not be found in the linked FFmpeg build.
    /// </summary>
    DecoderErrorCodecNotFound = -13,
    
    /// <summary>
    /// Failed to allocate a context for the audio decoder.
    /// </summary>
    DecoderErrorCodecContextAlloc = -14,
    
    /// <summary>
    /// The audio decoder could not be opened.
    /// </summary>
    DecoderErrorCodecOpenFailed = -15,
    
    /// <summary>
    /// The requested target sample format for decoding is invalid or not supported.
    /// </summary>
    DecoderErrorInvalidTargetFormat = -16,
    
    /// <summary>
    /// The audio resampler (swresample) could not be initialized for format conversion.
    /// </summary>
    DecoderErrorResamplerInitFailed = -17,
    
    /// <summary>
    /// Failed to allocate an AVFrame or AVPacket for the decoding process.
    /// </summary>
    DecoderErrorPacketFrameAlloc = -18,
    
    /// <summary>
    /// The seek operation failed in the underlying FFmpeg format context.
    /// </summary>
    DecoderErrorSeekFailed = -19,
    
    /// <summary>
    /// An unrecoverable error occurred during the decoding process, such as from a corrupt packet.
    /// </summary>
    DecoderErrorDecodingFailed = -20,

    // Encoder-specific Errors
    
    /// <summary>
    /// The requested output format (e.g., "mp3", "flac") could not be found.
    /// </summary>
    EncoderErrorFormatNotFound = -30,
    
    /// <summary>
    /// An encoder for the requested output format could not be found in the linked FFmpeg build.
    /// </summary>
    EncoderErrorCodecNotFound = -31,
    
    /// <summary>
    /// Failed to create a new audio stream in the output format context.
    /// </summary>
    EncoderErrorStreamAlloc = -32,
    
    /// <summary>
    /// Failed to allocate a context for the audio encoder.
    /// </summary>
    EncoderErrorCodecContextAlloc = -33,
    
    /// <summary>
    /// The audio encoder could not be opened.
    /// </summary>
    EncoderErrorCodecOpenFailed = -34,
    
    /// <summary>
    /// Failed to copy encoder parameters to the output stream.
    /// </summary>
    EncoderErrorContextParams = -35,
    
    /// <summary>
    /// Failed to write the header for the output audio file.
    /// </summary>
    EncoderErrorWriteHeader = -36,
    
    /// <summary>
    /// The provided input sample format for encoding is invalid or not supported.
    /// </summary>
    EncoderErrorInvalidInputFormat = -37,
    
    /// <summary>
    /// The audio resampler (swresample) could not be initialized for encoding.
    /// </summary>
    EncoderErrorResamplerInitFailed = -38,
    
    /// <summary>
    /// Failed to allocate an AVFrame or AVPacket for the encoding process.
    /// </summary>
    EncoderErrorPacketFrameAlloc = -39,

    /// <summary>
    /// An unrecoverable error occurred during the encoding process.
    /// </summary>
    EncoderErrorEncodingFailed = -40,
    
    /// <summary>
    /// An I/O error occurred while writing the encoded data to the output stream.
    /// </summary>
    EncoderErrorWriteFailed = -41,
}