using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Structs;

namespace SoundFlow.Providers;

/// <summary>
///     Provides audio data from a file or stream.
/// </summary>
/// <remarks>Loads full audio directly to memory.</remarks>
public sealed class AssetDataProvider : ISoundDataProvider
{
    private float[] _data;
    private int _samplePosition;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AssetDataProvider" /> class.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="stream">The stream to read audio data from.</param>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    public AssetDataProvider(AudioEngine engine, AudioFormat format, Stream stream)
    {
        var decoder = engine.CreateDecoder(stream, format);
        _data = Decode(decoder);
        decoder.Dispose();
        SampleRate = decoder.SampleRate;
        Length = _data.Length;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AssetDataProvider" /> class.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    /// <param name="data">The audio data to read.</param>
    public AssetDataProvider(AudioEngine engine, AudioFormat format, byte[] data)
        : this(engine, format, new MemoryStream(data))
    {
    }

    /// <inheritdoc />
    public int Position => _samplePosition;

    /// <inheritdoc />
    public int Length { get; } // Length in samples

    /// <inheritdoc />
    public bool CanSeek => true;

    /// <inheritdoc />
    public SampleFormat SampleFormat { get; private set; }
    
    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public bool IsDisposed { get; set; }

    /// <inheritdoc />
    public event EventHandler<EventArgs>? EndOfStreamReached;
    
    /// <inheritdoc />
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    /// <inheritdoc />
    public int ReadBytes(Span<float> buffer)
    {
        var samplesToRead = Math.Min(buffer.Length, _data.Length - _samplePosition);
        _data.AsSpan(_samplePosition, samplesToRead).CopyTo(buffer);

        _samplePosition += samplesToRead;

        if (_samplePosition >= _data.Length) 
            EndOfStreamReached?.Invoke(this, EventArgs.Empty);

        PositionChanged?.Invoke(this, new PositionChangedEventArgs(_samplePosition));

        return samplesToRead;
    }

    /// <inheritdoc />
    public void Seek(int sampleOffset)
    {
        _samplePosition = Math.Clamp(sampleOffset, 0, _data.Length);
        PositionChanged?.Invoke(this, new PositionChangedEventArgs(_samplePosition));
    }

    private float[] Decode(ISoundDecoder decoder)
    {
        SampleFormat = decoder.SampleFormat;
        return decoder.Length > 0 ? DecodeKnownLength(decoder) : DecodeUnknownLength(decoder);
    }

    private static float[] DecodeKnownLength(ISoundDecoder decoder)
    {
        var samples = new float[decoder.Length];
        var read = decoder.Decode(samples);
        if (read != decoder.Length)
            throw new InvalidOperationException($"Decoding error: Read {read}, expected {decoder.Length} samples.");
        return samples;
    }

    private static float[] DecodeUnknownLength(ISoundDecoder decoder)
    {
        const int blockSize = 22050;
        var blocks = new List<float[]>();
        int samplesRead;
        do
        {
            var block = new float[blockSize * decoder.Channels];
            samplesRead = decoder.Decode(block);
            if (samplesRead > 0) blocks.Add(block);
        } while (samplesRead == blockSize * decoder.Channels);

        var totalSamples = blocks.Sum(block => block.Length);
        var samples = new float[totalSamples];
        var offset = 0;
        foreach (var block in blocks)
        {
            block.CopyTo(samples, offset);
            offset += block.Length;
        }
        return samples;

    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed) return;

        // Dispose of _data
        _data = null!;
        IsDisposed = true;
    }

}
