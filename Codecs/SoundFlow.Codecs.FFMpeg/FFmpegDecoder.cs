using SoundFlow.Codecs.FFMpeg.Enums;
using SoundFlow.Codecs.FFMpeg.Exceptions;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Structs;
using SoundFlow.Codecs.FFMpeg.Native;
using SoundFlow.Utils;

namespace SoundFlow.Codecs.FFMpeg;

/// <summary>
/// An <see cref="ISoundDecoder"/> implementation that uses a native FFmpeg wrapper
/// to decode various audio formats.
/// </summary>
internal sealed class FFmpegDecoder : ISoundDecoder
{
    private readonly SafeDecoderHandle _handle;
    private readonly Stream _stream;
    private readonly FFmpeg.ReadCallback _readCallback;
    private readonly FFmpeg.SeekCallback _seekCallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="FFmpegDecoder"/> class.
    /// </summary>
    /// <param name="stream">The input stream containing the audio data to decode.</param>
    /// <param name="targetFormat">The desired output format for the decoded PCM data.</param>
    public FFmpegDecoder(Stream stream, AudioFormat targetFormat)
    {
        _stream = stream;

        _readCallback = OnRead;
        _seekCallback = OnSeek;

        _handle = FFmpeg.CreateDecoder();
        if (_handle.IsInvalid)
            throw new InvalidOperationException("Failed to create FFmpeg decoder handle.");

        var result = FFmpeg.InitializeDecoder(_handle, _readCallback, _seekCallback, IntPtr.Zero,
            targetFormat.Format, out var nativeFormat, out var channels, out var sampleRate);

        if (result != FFmpegResult.Success)
        {
            _handle.Dispose();
            throw new FFmpegException(result, $"Failed to initialize FFmpeg decoder. Result: {result}");
        }

        SampleFormat = targetFormat.Format = nativeFormat;
        Channels = targetFormat.Channels = (int)channels;
        SampleRate = targetFormat.SampleRate = (int)sampleRate;
        
        var lengthInFrames = FFmpeg.GetLengthInPcmFrames(_handle);
        if (lengthInFrames < 0)
        {
            _handle.Dispose();
            throw new InvalidOperationException("Failed to get stream length; the decoder handle may be invalid.");
        }
        Length = (int)(lengthInFrames * Channels);
    }
    
    /// <inheritdoc />
    public bool IsDisposed => _handle.IsClosed;
    
    /// <inheritdoc />
    public int Length { get; }
    
    /// <inheritdoc />
    public SampleFormat SampleFormat { get; }
    
    /// <inheritdoc />
    public int Channels { get; }
    
    /// <inheritdoc />
    public int SampleRate { get; }
    
    /// <inheritdoc />
    public event EventHandler<EventArgs>? EndOfStreamReached;

    /// <inheritdoc />
    public unsafe int Decode(Span<float> samples)
    {
        if (IsDisposed || samples.IsEmpty) return 0;

        var framesToRead = samples.Length / Channels;
        long framesRead;

        fixed (float* pSamples = samples)
        {
            var result = FFmpeg.ReadPcmFrames(_handle, (IntPtr)pSamples, framesToRead, out framesRead);
            if (result != FFmpegResult.Success)
            {
                throw new FFmpegException(result, $"An unrecoverable error occurred during decoding. Result: {result}");
            }
        }
        
        var samplesRead = (int)framesRead * Channels;
        if (samplesRead == 0)
        {
            EndOfStreamReached?.Invoke(this, EventArgs.Empty);
        }

        return samplesRead;
    }

    /// <inheritdoc />
    public bool Seek(int sampleOffset)
    {
        if (IsDisposed || !_stream.CanSeek) return false;

        var frameIndex = sampleOffset / Channels;
        var result = FFmpeg.SeekToPcmFrame(_handle, frameIndex);
        return result == FFmpegResult.Success;
    }

    private unsafe nuint OnRead(IntPtr pUserData, IntPtr pBuffer, nuint bytesToRead)
    {
        try
        {
            var buffer = new Span<byte>((void*)pBuffer, (int)bytesToRead);
            return (nuint)_stream.Read(buffer);
        }
        catch
        {
            Log.Critical("[FFmpegDecoder] Failed to read from stream.");
            // Signal error/EOF to FFmpeg by returning 0. FFmpeg will handle this gracefully as AVERROR_EOF.
            return 0;
        }
    }

    private long OnSeek(IntPtr pUserData, long offset, SeekWhence whence)
    {
        try
        {
            if (!_stream.CanSeek) return -1;
            return _stream.Seek(offset, (SeekOrigin)whence);
        }
        catch
        {
            Log.Critical("[FFmpegDecoder] Failed to seek stream.");
            return -1;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed) return;
        _handle.Dispose();

        // Ensure the delegates are not collected while the native code might still be using them.
        GC.KeepAlive(_readCallback);
        GC.KeepAlive(_seekCallback);
    }
}