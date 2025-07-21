using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Structs;

namespace SoundFlow.Providers;

/// <summary>
///     Provides audio data from a stream.
/// </summary>
public sealed class StreamDataProvider : ISoundDataProvider
{
    private readonly ISoundDecoder _decoder;
    private readonly Stream _stream;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StreamDataProvider" /> class.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="format">The audio format.</param>
    /// <param name="stream">The stream to read audio data from.</param>
    public StreamDataProvider(AudioEngine engine, AudioFormat format, Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _decoder = engine.CreateDecoder(stream, format);
        SampleRate = _decoder.SampleRate;

        _decoder.EndOfStreamReached += (_, args) =>
            EndOfStreamReached?.Invoke(this, args);
    }

    /// <inheritdoc />
    public int Position { get; private set; }

    /// <inheritdoc />
    public int Length => _decoder.Length;

    /// <inheritdoc />
    public bool CanSeek => _stream.CanSeek;

    /// <inheritdoc />
    public SampleFormat SampleFormat => _decoder.SampleFormat;

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
        var count = _decoder.Decode(buffer);
        Position += count;
        return count;
    }

    /// <inheritdoc />
    public void Seek(int sampleOffset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        
        if (!CanSeek)
            throw new InvalidOperationException("Seeking is not supported for this stream.");

        if (sampleOffset < 0 || sampleOffset > Length)
            throw new ArgumentOutOfRangeException(nameof(sampleOffset), "Seek position is outside the valid range.");

        _decoder.Seek(sampleOffset);
        Position = sampleOffset;
        
        PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed) return;
        _decoder.Dispose();
        _stream.Dispose();
        IsDisposed = true;
    }
}
