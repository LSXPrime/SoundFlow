using SoundFlow.Codecs.FFMpeg.Enums;
using SoundFlow.Codecs.FFMpeg.Exceptions;
using SoundFlow.Interfaces;
using SoundFlow.Structs;
using SoundFlow.Codecs.FFMpeg.Native;
using SoundFlow.Utils;

namespace SoundFlow.Codecs.FFMpeg;

/// <summary>
/// An <see cref="ISoundEncoder"/> implementation that uses a native FFmpeg wrapper
/// to encode raw PCM data into various audio formats.
/// </summary>
internal sealed class FFmpegEncoder : ISoundEncoder
{
    private readonly SafeEncoderHandle _handle;
    private readonly Stream _stream;
    private readonly FFmpeg.WriteCallback _writeCallback;
    private readonly int _channels;

    /// <summary>
    /// Initializes a new instance of the <see cref="FFmpegEncoder"/> class.
    /// </summary>
    /// <param name="stream">The output stream to write the encoded audio data to.</param>
    /// <param name="formatId">The string identifier of the target audio format (e.g., "mp3", "flac").</param>
    /// <param name="sourceFormat">The format of the raw PCM input data.</param>
    public FFmpegEncoder(Stream stream, string formatId, AudioFormat sourceFormat)
    {
        _stream = stream;
        _channels = sourceFormat.Channels;
        _writeCallback = OnWrite;
        
        _handle = FFmpeg.CreateEncoder();
        if (_handle.IsInvalid)
            throw new InvalidOperationException("Failed to create FFmpeg encoder handle.");
        
        var result = FFmpeg.InitializeEncoder(_handle, formatId, _writeCallback, IntPtr.Zero,
            sourceFormat.Format, (uint)sourceFormat.Channels, (uint)sourceFormat.SampleRate);
            
        if (result != FFmpegResult.Success)
        {
            _handle.Dispose();
            throw new FFmpegException(result, $"Failed to initialize FFmpeg encoder for format '{formatId}'. Result: {result}");
        }
    }

    /// <inheritdoc />
    public bool IsDisposed => _handle.IsClosed;

    /// <inheritdoc />
    public unsafe int Encode(Span<float> samples)
    {
        if (IsDisposed || samples.IsEmpty) return 0;
        
        var frameCount = samples.Length / _channels;
        long framesWritten;

        fixed (float* pSamples = samples)
        {
            var result = FFmpeg.WritePcmFrames(_handle, (IntPtr)pSamples, frameCount, out framesWritten);
            if (result != FFmpegResult.Success)
            {
                throw new FFmpegException(result, $"An unrecoverable error occurred during encoding. Result: {result}");
            }
        }

        return (int)framesWritten * _channels;
    }

    private unsafe nuint OnWrite(IntPtr pUserData, IntPtr pBuffer, nuint bytesToWrite)
    {
        try
        {
            var buffer = new ReadOnlySpan<byte>((void*)pBuffer, (int)bytesToWrite);
            _stream.Write(buffer);
            return bytesToWrite;
        }
        catch
        {
            Log.Critical("[FFmpegEncoder] Failed to write to stream.");
            return 0;
        }
    }
    
    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed) return;
        _handle.Dispose();
        GC.KeepAlive(_writeCallback);
    }
}