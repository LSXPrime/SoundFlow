using System.Buffers;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;

namespace SoundFlow.Providers;

/// <summary>
///     Provides audio data from a file or stream by reading in chunks.
/// </summary>
/// <remarks>
///     Efficiently handles large audio files by reading and decoding audio data in manageable chunks.
/// </remarks>
public sealed class ChunkedDataProvider : ISoundDataProvider
{
    private const int DefaultChunkSize = 220500; // Number of samples per channel (2205 ms at 44.1 kHz = 220500 samples = 10 second)

    private readonly Stream _stream;
    private ISoundDecoder _decoder;
    private readonly int _chunkSize;

    private readonly Queue<float> _buffer = new();
    private bool _isEndOfStream;
    private int _samplePosition;

    private readonly object _lock = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChunkedDataProvider" /> class.
    /// </summary>
    /// <param name="stream">The stream to read audio data from.</param>
    /// <param name="chunkSize">The number of samples to read in each chunk.</param>
    public ChunkedDataProvider(Stream stream, int chunkSize = DefaultChunkSize)
    {
        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");

        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _chunkSize = chunkSize;

        _decoder = AudioEngine.Instance.CreateDecoder(_stream);

        SampleFormat = _decoder.SampleFormat;
        SampleRate = AudioEngine.Instance.SampleRate;
        CanSeek = _stream.CanSeek;

        // Begin prefetching data
        FillBuffer();
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChunkedDataProvider" /> class.
    /// </summary>
    /// <param name="filePath">The path to the file to read audio data from.</param>
    /// <param name="chunkSize">The number of samples to read in each chunk.</param>
    public ChunkedDataProvider(string filePath, int chunkSize = DefaultChunkSize)
        : this(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read), chunkSize)
    {
    }

    /// <inheritdoc />
    public int Position
    {
        get
        {
            lock (_lock)
            {
                return _samplePosition;
            }
        }
    }
    
    /// <inheritdoc />
    public int Length => _decoder.Length;

    /// <inheritdoc />
    public bool CanSeek { get; }

    /// <inheritdoc />
    public SampleFormat SampleFormat { get; }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public event EventHandler<EventArgs>? EndOfStreamReached;
    
    /// <inheritdoc />
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    /// <inheritdoc />
    public int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed) return 0;
        var samplesRead = 0;

        lock (_lock)
        {
            while (samplesRead < buffer.Length)
            {
                if (_buffer.Count == 0)
                {
                    if (_isEndOfStream)
                    {
                        // End of stream reached
                        EndOfStreamReached?.Invoke(this, EventArgs.Empty);
                        break;
                    }

                    // Fill buffer with more data
                    FillBuffer();
                    if (_buffer.Count == 0)
                    {
                        // No more data to read
                        _isEndOfStream = true;
                        EndOfStreamReached?.Invoke(this, EventArgs.Empty);
                        break;
                    }
                }

                buffer[samplesRead++] = _buffer.Dequeue();
            }

            _samplePosition += samplesRead;

            PositionChanged?.Invoke(this, new PositionChangedEventArgs(_samplePosition));
        }

        return samplesRead;
    }

    /// <inheritdoc />
    public void Seek(int sampleOffset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!CanSeek)
            throw new NotSupportedException("Seeking is not supported on the underlying stream or decoder.");

        lock (_lock)
        {
            // Clamp the sample offset to valid range
            sampleOffset = Math.Clamp(sampleOffset, 0, Length);

            // Reset decoder and seek stream to the new position
            _decoder.Dispose();

            // Create a new decoder starting from the new position
            _decoder = AudioEngine.Instance.CreateDecoder(_stream);
            
            _decoder.Seek(sampleOffset);
            
            // Clear the existing buffer
            _buffer.Clear();
            _isEndOfStream = false;
            
            // Update the sample position
            _samplePosition = sampleOffset;

            PositionChanged?.Invoke(this, new PositionChangedEventArgs(_samplePosition));

            // Refill the buffer from the new position
            FillBuffer();
        }
    }

    private void FillBuffer()
    {
        if (IsDisposed)
            return;

        var samplesToRead = _chunkSize * AudioEngine.Channels;
        var buffer = ArrayPool<float>.Shared.Rent(samplesToRead);

        try
        {
            var samplesRead = _decoder.Decode(buffer);

            if (samplesRead > 0)
            {
                for (var i = 0; i < samplesRead; i++)
                {
                    _buffer.Enqueue(buffer[i]);
                }
            }
            else
            {
                _isEndOfStream = true;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed)
            return;

        lock (_lock)
        {
            _decoder.Dispose();
            _stream.Dispose();
            _buffer.Clear();

            IsDisposed = true;
        }
    }
}