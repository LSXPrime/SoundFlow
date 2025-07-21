using System.Buffers;
using System.Runtime.InteropServices;
using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Enums;
using SoundFlow.Exceptions;
using SoundFlow.Interfaces;

namespace SoundFlow.Backends.MiniAudio;

/// <summary>
///     An object to assist with converting audio formats into raw PCM frames.
/// </summary>
internal sealed unsafe class MiniAudioDecoder : ISoundDecoder
{
    private readonly nint _decoder;
    private readonly Stream _stream;
    private readonly Native.BufferProcessingCallback _readCallback;
    private readonly Native.SeekCallback _seekCallbackCallback;
    private bool _endOfStreamReached;
    private byte[] _readBuffer = [];
    private readonly object _syncLock = new();

    /// <summary>
    ///     Constructs a new decoder from the given stream in one of the supported formats.
    /// </summary>
    /// <param name="stream">A stream to a file or streaming audio source in one of the supported formats.</param>
    /// <param name="sampleFormat">The format of the audio samples.</param>
    /// <param name="channels">The number of audio channels.</param>
    /// <param name="sampleRate">The sample rate of the audio.</param>
    public MiniAudioDecoder(Stream stream, SampleFormat sampleFormat, int channels, int sampleRate)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        SampleFormat = sampleFormat;
        Channels = channels;
        SampleRate = sampleRate;

        var configPtr = Native.AllocateDecoderConfig(SampleFormat, (uint)Channels, (uint)SampleRate);

        _decoder = Native.AllocateDecoder();
        var result = Native.DecoderInit(_readCallback = ReadCallback, _seekCallbackCallback = SeekCallback, nint.Zero, configPtr, _decoder);
        Native.Free(configPtr);

        if (result != Result.Success) throw new BackendException("MiniAudio", result, "Unable to initialize decoder.");

        result = Native.DecoderGetLengthInPcmFrames(_decoder, out var length);
        if (result != Result.Success) throw new BackendException("MiniAudio", result, "Unable to get decoder length.");
        Length = (int)length * Channels;
        _endOfStreamReached = false;
    }
    
    /// <inheritdoc />
    public int Channels { get; }
    
    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public int Length { get; private set; }

    /// <inheritdoc />
    public SampleFormat SampleFormat { get; }

    public event EventHandler<EventArgs>? EndOfStreamReached;

    /// <summary>
    ///     Decodes the next several samples.
    /// </summary>
    public int Decode(Span<float> samples)
    {
        lock (_syncLock)
        {
            if (IsDisposed || _endOfStreamReached)
                return 0;

            var framesToRead = (uint)(samples.Length / Channels);
            if (framesToRead == 0)
            {
                _endOfStreamReached = true;
                EndOfStreamReached?.Invoke(this, EventArgs.Empty);
                return 0;
            }
            
            var buffer = GetBufferIfNeeded(samples.Length);

            var span = buffer ?? MemoryMarshal.AsBytes(samples);

            ulong framesRead;
            fixed (byte* nativeBuffer = span)
            {
                if (Native.DecoderReadPcmFrames(_decoder, (nint)nativeBuffer, framesToRead, out framesRead) != Result.Success ||
                    framesRead == 0)
                {
                    _endOfStreamReached = true;
                    EndOfStreamReached?.Invoke(this, EventArgs.Empty);
                    return 0;
                }
            }
            
            if (SampleFormat is not SampleFormat.F32)
            {
                ConvertToFloat(samples, framesRead, span);
            }

            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return (int)framesRead * Channels;
        }
    }

    private byte[]? GetBufferIfNeeded(int sampleLength)
    {
        // U32 can be done in-place with the passed in float span
        if (SampleFormat is SampleFormat.F32 or SampleFormat.S32)
        {
            return null;
        }
        var byteSize = SampleFormat switch
        {
            SampleFormat.S16 => sampleLength * 2,
            SampleFormat.S24 => sampleLength * 3,
            SampleFormat.U8 => sampleLength,
            _ => throw new NotSupportedException($"Sample format {SampleFormat} is not supported.")
        };
        return ArrayPool<byte>.Shared.Rent(byteSize);
    }

    private void ConvertToFloat(Span<float> samples, ulong framesRead, Span<byte> nativeBuffer)
    {
        var sampleCount = checked((int)framesRead * Channels);
        switch (SampleFormat)
        {
            case SampleFormat.S16:
                var shortSpan = MemoryMarshal.Cast<byte, short>(nativeBuffer);
                for (var i = 0; i < sampleCount; i++)
                    samples[i] = shortSpan[i] / (float)short.MaxValue;
                break;
            case SampleFormat.S24:
                for (var i = 0; i < sampleCount; i++)
                {
                    var sample24 = (nativeBuffer[i * 3] << 0) | (nativeBuffer[i * 3 + 1] << 8) | (nativeBuffer[i * 3 + 2] << 16);
                    if ((sample24 & 0x800000) != 0) // Sign extension for negative values
                        sample24 |= unchecked((int)0xFF000000);
                    samples[i] = sample24 / 8388608f;
                }
                break;
            case SampleFormat.S32:
                var int32Span = MemoryMarshal.Cast<byte, int>(nativeBuffer);
                for (var i = 0; i < sampleCount; i++)
                    samples[i] = int32Span[i] / (float)int.MaxValue;
                break;
            case SampleFormat.U8:
                for (var i = 0; i < sampleCount; i++)
                    samples[i] = (nativeBuffer[i] - 128) / 128f; // Scale U8 to -1.0 to 1.0
                break;
        }
    }

    /// <summary>
    ///     Seek to start decoding at the given offset.
    /// </summary>
    public bool Seek(int offset)
    {
        lock (_syncLock)
        {
            Result result;
            if (Length == 0)
            {
                result = Native.DecoderGetLengthInPcmFrames(_decoder, out var length);
                if (result != Result.Success || (int)length == 0) return false;
                Length = (int)length * Channels;
            }

            _endOfStreamReached = false;
            result = Native.DecoderSeekToPcmFrame(_decoder, (ulong)(offset / Channels));
            return result == Result.Success;
        }
    }

    /// <summary>
    /// Disposes of the decoder resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~MiniAudioDecoder()
    {
        Dispose(false);
    }

    private Result ReadCallback(nint pDecoder, nint pBufferOut, ulong bytesToRead, out ulong* pBytesRead)
    {
        lock (_syncLock)
        {
            if (!_stream.CanRead || _endOfStreamReached)
            {
                pBytesRead = (ulong*)0;
                return Result.NoDataAvailable;
            }

            // Read the next chunk of bytes
            var size = (int)bytesToRead;
            if (_readBuffer.Length < size)
                Array.Resize(ref _readBuffer, size);

            var read = _stream.Read(_readBuffer, 0, size);
            // Check for end of stream
            if (read == 0 && !_endOfStreamReached)
            {
                _endOfStreamReached = true;
                EndOfStreamReached?.Invoke(this, EventArgs.Empty);
            }

            // Copy from read buffer to write buffer
            fixed (byte* pReadBuffer = _readBuffer)
            {
                Buffer.MemoryCopy(pReadBuffer, (void*)pBufferOut, size, read);
            }

            // Clear read buffer
            Array.Clear(_readBuffer, 0, _readBuffer.Length);

            pBytesRead = (ulong*)read;
            return Result.Success;
        }
    }

    private Result SeekCallback(nint _, long byteOffset, SeekPoint point)
    {
        lock (_syncLock)
        {
            if (!_stream.CanSeek)
                return Result.NoDataAvailable;

            if (byteOffset >= 0 && byteOffset < _stream.Length - 1)
                _stream.Seek(byteOffset, point == SeekPoint.FromCurrent ? SeekOrigin.Current : SeekOrigin.Begin);

            return Result.Success;
        }
    }

    private void Dispose(bool _)
    {
        lock (_syncLock)
        {
            if (IsDisposed) return;

            // keep delegates alive
            GC.KeepAlive(_readCallback);
            GC.KeepAlive(_seekCallbackCallback);

            Native.DecoderUninit(_decoder);
            Native.Free(_decoder);

            IsDisposed = true;
        }
    }
}
