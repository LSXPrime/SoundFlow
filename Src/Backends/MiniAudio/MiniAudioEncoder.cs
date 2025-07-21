using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Enums;
using SoundFlow.Exceptions;
using SoundFlow.Interfaces;

namespace SoundFlow.Backends.MiniAudio;

/// <summary>
/// An object to assist with encoding raw PCM frames into audio formats.
/// </summary>
internal sealed unsafe class MiniAudioEncoder : ISoundEncoder
{
    private readonly nint _encoder;
    private readonly Stream _stream;
    private readonly Native.BufferProcessingCallback _writeCallback;
    private readonly Native.SeekCallback _seekCallback;
    private readonly object _syncLock = new();
    private readonly int _channels;

    /// <summary>
    /// Constructs a new encoder to write to the given stream in the specified format.
    /// </summary>
    /// <param name="stream">The stream to write encoded audio to.</param>
    /// <param name="encodingFormat">The desired audio encoding format.</param>
    /// <param name="sampleFormat">The format of the input audio samples.</param>
    /// <param name="channels">The number of audio channels.</param>
    /// <param name="sampleRate">The sample rate of the input audio.</param>
    public MiniAudioEncoder(Stream stream, EncodingFormat encodingFormat, SampleFormat sampleFormat, int channels,
        int sampleRate)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _channels = channels;

        if (encodingFormat != EncodingFormat.Wav)
            throw new NotSupportedException("MiniAudio only supports WAV encoding.");
        
        // Construct encoder config
        var config = Native.AllocateEncoderConfig(encodingFormat, sampleFormat, (uint)channels, (uint)sampleRate);

        // Allocate encoder and initialize
        _encoder = Native.AllocateEncoder();
        var result = Native.EncoderInit(_writeCallback = WriteCallback, _seekCallback = SeekCallback, nint.Zero, config, _encoder);
        Native.Free(config);
        
        if (result != Result.Success)
            throw new BackendException("MiniAudio", result, "Unable to initialize encoder.");
    }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Encodes the given samples and writes them to the output stream.
    /// </summary>
    /// <param name="samples">The buffer containing the PCM samples to encode.</param>
    /// <returns>The number of samples successfully encoded.</returns>
    public int Encode(Span<float> samples)
    {
        lock (_syncLock)
        {
            if (IsDisposed)
                return 0;

            var framesToWrite = (ulong)(samples.Length / _channels);
            ulong framesWritten = 0;

            fixed (float* pSamples = samples)
            {
                var result = Native.EncoderWritePcmFrames(_encoder, (nint)pSamples, framesToWrite, &framesWritten);
                if (result != Result.Success)
                    throw new BackendException("MiniAudio", result, "Failed to write PCM frames to encoder.");
            }

            return (int)framesWritten * _channels;
        }
    }

    /// <summary>
    /// Disposes of the encoder resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalizer for the <see cref="MiniAudioEncoder"/> class.
    /// </summary>
    ~MiniAudioEncoder()
    {
        Dispose(false);
    }

    /// <summary>
    /// Callback method for MiniAudio to write encoded data to the stream.
    /// MiniAudio provides the encoded data in <paramref name="pBufferIn"/>,
    /// which is then written to the internal <see cref="_stream"/>.
    /// </summary>
    private Result WriteCallback(nint pEncoder, nint pBufferIn, ulong bytesToWrite, out ulong* pBytesWritten)
    {
        lock (_syncLock)
        {
            if (!_stream.CanWrite)
            {
                pBytesWritten = (ulong*)0;
                return Result.NoDataAvailable;
            }

            var bytes = new ReadOnlySpan<byte>((void*)pBufferIn, (int)bytesToWrite);
            _stream.Write(bytes);
            
            pBytesWritten = (ulong*)bytesToWrite;
            return Result.Success;
        }
    }

    /// <summary>
    /// Callback method for MiniAudio to seek the output stream.
    /// </summary>
    private Result SeekCallback(nint pEncoder, long byteOffset, SeekPoint point)
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

            // Keep delegates alive
            GC.KeepAlive(_writeCallback);
            GC.KeepAlive(_seekCallback);

            Native.EncoderUninit(_encoder);
            Native.Free(_encoder);

            IsDisposed = true;
        }
    }
}
