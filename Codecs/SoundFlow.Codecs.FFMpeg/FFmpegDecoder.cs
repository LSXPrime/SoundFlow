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

    private int? _pendingSeekOffset;

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
            targetFormat.Format, out var nativeFormat, out var channels, out var sampleRate,
            out var timebaseNum, out var timebaseDen);

        //Console.WriteLine($"Timebase {timebaseNum} / {timebaseDen}");

        if (result != FFmpegResult.Success)
        {
            var logMessage = $"Failed to initialize FFmpeg decoder. Result: {result}";
            Log.Error(logMessage);
            _handle.Dispose();
            throw new FFmpegException(result, logMessage);
        }

        SampleFormat = targetFormat.Format = nativeFormat;
        Channels = targetFormat.Channels = (int)channels;
        SampleRate = targetFormat.SampleRate = (int)sampleRate;
        
        var lengthInFrames = FFmpeg.GetLengthInPcmFrames(_handle);
        if (lengthInFrames < 0)
        {
            const string logMessage = "Failed to get stream length, the decoder handle may be invalid.";
            Log.Error(logMessage);
            _handle.Dispose();
            throw new InvalidOperationException(logMessage);
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

        long framesToRead = samples.Length / Channels;

        long totalFramesRead = 0;
        long framePosition = -1;
        long backwardsCompensation = 1024;

        do
        {
            fixed (float* pSamples = samples)
            {
                var result = FFmpeg.ReadPcmFrames(_handle, (IntPtr)pSamples, framesToRead, out var framesRead, out var startFrameIndex);
                if (result != FFmpegResult.Success)
                {
                    throw new FFmpegException(result, $"An unrecoverable error occurred during decoding. Result: {result}");
                }

                // If we reached the end, we just stop
                if (framesRead == 0)
                    break;

                if(_pendingSeekOffset != null)
                {
                    // Initialize the frame position if it hasn't been
                    // We're only guaranteed to have startFrameIndex immediately after the seek, so afterwards we need to keep track
                    // of it ourselves
                    if(framePosition < 0)
                    {
                        // If there's a precise seek pending, the decoder is expected to provide startFrameIndex, so we know how many samples we need to skip
                        // If that hasn't been provided, something went wrong inside of the decoder
                        if (startFrameIndex < 0)
                            throw new FFmpegException(result, $"Pending seek to location {_pendingSeekOffset}, but failed to retrieve startFrameIndex");

                        framePosition = startFrameIndex;

                        if (framePosition > _pendingSeekOffset.Value)
                        {
                            // The frame position is ahead of what we asked for.
                            // This is probably because the seek went too far ahead with the "priming packet"
                            // We need to compensate and re-seek further backwards and then work our way towards this packets
                            result = FFmpeg.SeekToPcmFrame(_handle, framePosition - backwardsCompensation);
                            backwardsCompensation *= 2;

                            if (result != FFmpegResult.Success)
                                throw new FFmpegException(result, $"Failed to re-seek backwards. Pending offset: {_pendingSeekOffset}");

                            // Reset this, because we're seeking back again
                            framePosition = -1;

                            continue;
                        }
                    }

                    // Compute how many frames we need to discard
                    var skip = Math.Max(0, _pendingSeekOffset.Value - framePosition);

                    // Adjust for number of samples by channels (the value is frames)
                    var skipSamples = skip * Channels;

                    if(skipSamples < samples.Length)
                    {
                        // We have some valid data in the buffer! We just need to move this back to the front and then read the rest
                        var validSamples = samples.Slice((int)skipSamples);
                        validSamples.CopyTo(samples);

                        // Trim the samples so we can read the rest
                        samples = samples.Slice(validSamples.Length);
                    }

                    // Advance the position by however many frames we have read
                    framePosition += framesRead;

                    // Adjust the frames read by skipped samples
                    framesRead -= skip;
                    framesRead = Math.Max(framesRead, 0);
                }

                totalFramesRead += framesRead;
                framesToRead -= framesRead;
            }
        }
        while (framesToRead > 0);

        // We've done the read, the seek should be resolved at this point
        _pendingSeekOffset = null;

        var samplesRead = (int)totalFramesRead * Channels;
        if (samplesRead == 0)
        {
            EndOfStreamReached?.Invoke(this, EventArgs.Empty);
        }

        return samplesRead;
    }

    /// <inheritdoc />
    public bool Seek(int sampleOffset) => SeekInternal(sampleOffset, false);

    /// <inheritdoc />
    public bool PreciseSeek(int sampleOffset) => SeekInternal(sampleOffset, true);

    private bool SeekInternal(int sampleOffset, bool precise)
    {
        if (IsDisposed || !_stream.CanSeek) return false;

        var frameIndex = sampleOffset / Channels;
        var result = FFmpeg.SeekToPcmFrame(_handle, frameIndex);

        if (result != FFmpegResult.Success)
            return false;

        if (precise)
            _pendingSeekOffset = frameIndex;
        else
            _pendingSeekOffset = null;

        return true;
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
            Log.Critical("Failed to read from stream.");
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
            Log.Critical("Failed to seek stream.");
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