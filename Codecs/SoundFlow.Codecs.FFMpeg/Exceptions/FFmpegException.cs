using SoundFlow.Codecs.FFMpeg.Enums;
using SoundFlow.Codecs.FFMpeg.Native;
using SoundFlow.Exceptions;

namespace SoundFlow.Codecs.FFMpeg.Exceptions;

/// <summary>
/// Represents an error that occurred within the native FFmpeg Codecs library.
/// </summary>
/// <param name="result">The native result code that caused the exception.</param>
/// <param name="message">The message that describes the error.</param>
public sealed class FFmpegException(FFmpegResult result, string? message) : BackendException("FFmpeg", (int)result, message ?? GetErrorMessage(result))
{
    /// <summary>
    /// Gets the detailed result code from the native FFmpeg library.
    /// </summary>
    public FFmpegResult Result { get; } = result;
    
    private static string GetErrorMessage(FFmpegResult errorCode)
    {
        var errorText = FFmpeg.ResultToString(errorCode);
        return $"FFmpeg error ({errorCode}): {errorText}";
    }
}