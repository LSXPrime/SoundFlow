using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Metadata;
using SoundFlow.Metadata.Models;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Providers;

/// <summary>
///     Provides audio data from a stream.
/// </summary>
public sealed class StreamDataProvider : ISoundDataProvider
{
    private readonly ISoundDecoder _decoder;
    private readonly Stream _stream;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StreamDataProvider" /> class by automatically detecting the format.
    ///     It first attempts to read metadata; if that fails, it falls back to probing the stream with all available codecs.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="stream">The stream to read audio data from. Must be readable and seekable.</param>
    /// <param name="options">Optional configuration for metadata reading.</param>
    public StreamDataProvider(AudioEngine engine, Stream stream, ReadOptions? options = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        options ??= new ReadOptions();

        var formatInfoResult = SoundMetadataReader.Read(_stream, options);

        if (formatInfoResult is { IsSuccess: true, Value: not null })
        {
            // Path 1: Metadata read successfully. Use it to create the decoder.
            FormatInfo = formatInfoResult.Value;
            var discoveredFormat = new AudioFormat
            {
                Format = FormatInfo.BitsPerSample > 0 ? FormatInfo.BitsPerSample.GetSampleFormatFromBitsPerSample() : SampleFormat.F32,
                Channels = FormatInfo.ChannelCount,
                Layout = AudioFormat.GetLayoutFromChannels(FormatInfo.ChannelCount),
                SampleRate = FormatInfo.SampleRate
            };

            _stream.Position = 0;
            _decoder = engine.CreateDecoder(_stream, FormatInfo.FormatIdentifier, discoveredFormat);
        }
        else
        {
            // Path 2: Metadata read failed. Fall back to probing with codecs.
            _stream.Position = 0;
            _decoder = engine.CreateDecoder(_stream, out var detectedFormat);

            // Create a basic FormatInfo from what the decoder found.
            FormatInfo = new SoundFormatInfo
            {
                FormatName = "Unknown (Probed)",
                FormatIdentifier = "unknown",
                ChannelCount = detectedFormat.Channels,
                SampleRate = detectedFormat.SampleRate,
                Duration = _decoder.Length > 0 && detectedFormat.SampleRate > 0
                    ? TimeSpan.FromSeconds((double)_decoder.Length / (detectedFormat.SampleRate * detectedFormat.Channels))
                    : TimeSpan.Zero
            };
        }
        
        SampleRate = _decoder.SampleRate;
        _decoder.EndOfStreamReached += EndOfStreamReached;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="StreamDataProvider" /> class with a specified format.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="format">The audio format.</param>
    /// <param name="stream">The stream to read audio data from.</param>
    public StreamDataProvider(AudioEngine engine, AudioFormat format, Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        
        var formatInfoResult = SoundMetadataReader.Read(_stream, new ReadOptions
        {
            ReadTags = false, 
            ReadAlbumArt = false, 
            DurationAccuracy = DurationAccuracy.FastEstimate
        });
        
        if (formatInfoResult is { IsSuccess: true, Value: not null })
        {
            // Path 1: Metadata read successfully. Use the discovered formatId with the user's target format.
            FormatInfo = formatInfoResult.Value;
            _stream.Position = 0;
            _decoder = engine.CreateDecoder(stream, FormatInfo.FormatIdentifier, format);
        }
        else
        {
            // Path 2: Metadata read failed. Fall back to probing, providing the user's format as a hint.
            _stream.Position = 0;
            _decoder = engine.CreateDecoder(_stream, out var detectedFormat, format);
                
            // Create a basic FormatInfo from what the decoder found.
            FormatInfo = new SoundFormatInfo
            {
                FormatName = "Unknown (Probed)",
                FormatIdentifier = "unknown",
                ChannelCount = detectedFormat.Channels,
                SampleRate = detectedFormat.SampleRate,
                Duration = _decoder.Length > 0 && detectedFormat.SampleRate > 0
                    ? TimeSpan.FromSeconds((double)_decoder.Length / (detectedFormat.SampleRate * detectedFormat.Channels))
                    : TimeSpan.Zero
            };
        }
        
        SampleRate = _decoder.SampleRate;

        _decoder.EndOfStreamReached += EndOfStreamReached;
    }

    /// <inheritdoc />
    public int Position { get; private set; }

    /// <inheritdoc />
    public int Length => _decoder.Length > 0 || FormatInfo == null
        ? _decoder.Length
        : (int)(FormatInfo.Duration.TotalSeconds * FormatInfo.SampleRate * FormatInfo.ChannelCount);

    /// <inheritdoc />
    public bool CanSeek => _stream.CanSeek;

    /// <inheritdoc />
    public SampleFormat SampleFormat => _decoder.SampleFormat;

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public SoundFormatInfo? FormatInfo { get; }

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
        PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));
        return count;
    }

    /// <inheritdoc />
    public void Seek(int sampleOffset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (!CanSeek)
            throw new InvalidOperationException("Seeking is not supported for this stream.");

        if (sampleOffset < 0 || (Length > 0 && sampleOffset > Length))
            throw new ArgumentOutOfRangeException(nameof(sampleOffset), "Seek position is outside the valid range.");

        _decoder.Seek(sampleOffset);
        Position = sampleOffset;

        PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed) return;
        _decoder.EndOfStreamReached -= EndOfStreamReached;
        _decoder.Dispose();
        _stream.Dispose();
        IsDisposed = true;
    }
}