using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Utils;

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
    /// <param name="sampleFormat">The format of the input audio samples.</param>
    /// <param name="channels">The number of audio channels.</param>
    /// <param name="sampleRate">The sample rate of the input audio.</param>
    public MiniAudioEncoder(Stream stream, SampleFormat sampleFormat, int channels,
        int sampleRate)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _channels = channels;
        
        // Construct encoder config
        var config = Native.AllocateEncoderConfig(sampleFormat, (uint)channels, (uint)sampleRate);

        // Allocate encoder and initialize
        _encoder = Native.AllocateEncoder();
        
        // Store delegates to prevent GC
        _writeCallback = WriteCallback;
        _seekCallback = SeekCallback;
        
        var result = Native.EncoderInit(_writeCallback, _seekCallback, nint.Zero, config, _encoder);
        Native.Free(config);
        
        if (result != MiniAudioResult.Success)
            throw new MiniaudioException("MiniAudio", result, "Unable to initialize encoder.");
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

            fixed (float* pSamples = samples)
            {
                var result = Native.EncoderWritePcmFrames(_encoder, (nint)pSamples, framesToWrite, out var framesWritten);
                if (result != MiniAudioResult.Success)
                    throw new MiniaudioException("MiniAudio", result, "Failed to write PCM frames to encoder.");
                
                return (int)framesWritten * _channels;
            }
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
    private MiniAudioResult WriteCallback(nint pEncoder, nint pBufferIn, ulong bytesToWrite, out ulong pBytesWritten)
    {
        try
        {
            lock (_syncLock)
            {
                if (!_stream.CanWrite)
                {
                    pBytesWritten = 0;
                    return MiniAudioResult.NoDataAvailable;
                }

                // Create a span directly from the native pointer to avoid allocation
                var bytes = new ReadOnlySpan<byte>((void*)pBufferIn, (int)bytesToWrite);
                _stream.Write(bytes);
            
                pBytesWritten = bytesToWrite;
                return MiniAudioResult.Success;
            }
        }
        catch (Exception)
        {
            pBytesWritten = 0;
            Log.Critical("[MiniAudioEncoder] Failed to write PCM frames to encoder.");
            return MiniAudioResult.IoError;
        }
    }

    /// <summary>
    /// Callback method for MiniAudio to seek the output stream.
    /// </summary>
    private MiniAudioResult SeekCallback(nint pEncoder, long byteOffset, SeekPoint point)
    {
        try
        {
            lock (_syncLock)
            {
                if (!_stream.CanSeek)
                    return MiniAudioResult.NoDataAvailable;

                if (byteOffset >= 0 && byteOffset < _stream.Length - 1)
                    _stream.Seek(byteOffset, point == SeekPoint.FromCurrent ? SeekOrigin.Current : SeekOrigin.Begin);
            
                return MiniAudioResult.Success;
            }
        }
        catch (Exception)
        {
            Log.Critical("[MiniAudioEncoder] Failed to seek stream.");
            return MiniAudioResult.IoError;
        }
    }
    
    private void Dispose(bool _)
    {
        lock (_syncLock)
        {
            if (IsDisposed) return;

            Native.EncoderUninit(_encoder);
            Native.Free(_encoder);
            
            // Keep delegates alive
            GC.KeepAlive(_writeCallback);
            GC.KeepAlive(_seekCallback);

            IsDisposed = true;
        }
    }
}