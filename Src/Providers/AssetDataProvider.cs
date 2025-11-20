using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Metadata;
using SoundFlow.Metadata.Models;
using SoundFlow.Structs;

namespace SoundFlow.Providers;

/// <summary>
///     Provides audio data from a file or stream.
/// </summary>
/// <remarks>Loads full audio directly to memory.</remarks>
public sealed class AssetDataProvider : ISoundDataProvider
{
    private readonly float[] _data;
    private int _samplePosition;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AssetDataProvider" /> class by reading from a stream and detecting its format.
    ///     If metadata reading fails, it will attempt to probe the stream with registered codecs.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="stream">The stream to read audio data from.</param>
    /// <param name="options">Optional configuration for metadata reading.</param>
    public AssetDataProvider(AudioEngine engine, Stream stream, ReadOptions? options = null)
    {
        options ??= new ReadOptions();
        
        var formatInfoResult = SoundMetadataReader.Read(stream, options);
        ISoundDecoder decoder;

        if (formatInfoResult is { IsSuccess: true, Value: not null })
        {
            FormatInfo = formatInfoResult.Value;
            var discoveredFormat = new AudioFormat
            {
                Format = SampleFormat.F32,
                Channels = FormatInfo.ChannelCount,
                Layout = AudioFormat.GetLayoutFromChannels(FormatInfo.ChannelCount),
                SampleRate = FormatInfo.SampleRate
            };
            stream.Position = 0;
            decoder = engine.CreateDecoder(stream, FormatInfo.FormatIdentifier, discoveredFormat);
        }
        else
        {
            stream.Position = 0;
            decoder = engine.CreateDecoder(stream, out var detectedFormat);
            FormatInfo = new SoundFormatInfo
            {
                FormatName = "Unknown (Probed)",
                FormatIdentifier = "unknown",
                ChannelCount = detectedFormat.Channels,
                SampleRate = detectedFormat.SampleRate,
                Duration = decoder.Length > 0 && detectedFormat.SampleRate > 0
                    ? TimeSpan.FromSeconds((double)decoder.Length / (detectedFormat.SampleRate * detectedFormat.Channels))
                    : TimeSpan.Zero
            };
        }
        
        _data = Decode(decoder);
        decoder.Dispose();
        SampleRate = FormatInfo.SampleRate;
        Length = _data.Length;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AssetDataProvider" /> class with a specified format.
    ///     If metadata reading fails, it will attempt to probe the stream with registered codecs.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    /// <param name="stream">The stream to read audio data from.</param>
    public AssetDataProvider(AudioEngine engine, AudioFormat format, Stream stream)
    {
        var formatInfoResult = SoundMetadataReader.Read(stream, new ReadOptions
        {
            ReadTags = false, 
            ReadAlbumArt = false, 
            DurationAccuracy = DurationAccuracy.FastEstimate
        });
        
        ISoundDecoder decoder;
        if (formatInfoResult is { IsSuccess: true, Value: not null })
        {
            FormatInfo = formatInfoResult.Value;
            stream.Position = 0;
            decoder = engine.CreateDecoder(stream, FormatInfo.FormatIdentifier, format);
        }
        else
        {
            stream.Position = 0;
            decoder = engine.CreateDecoder(stream, out var detectedFormat, format);
            FormatInfo = new SoundFormatInfo
            {
                FormatName = "Unknown (Probed)",
                FormatIdentifier = "unknown",
                ChannelCount = detectedFormat.Channels,
                SampleRate = detectedFormat.SampleRate,
                Duration = decoder.Length > 0 && detectedFormat.SampleRate > 0
                    ? TimeSpan.FromSeconds((double)decoder.Length / (detectedFormat.SampleRate * detectedFormat.Channels))
                    : TimeSpan.Zero
            };
        }

        _data = Decode(decoder);
        decoder.Dispose();
        SampleRate = format.SampleRate;
        Length = _data.Length;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AssetDataProvider" /> class from a byte array.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="data">The byte array containing the audio file data.</param>
    /// <param name="options">Optional configuration for metadata reading.</param>
    public AssetDataProvider(AudioEngine engine, byte[] data, ReadOptions? options = null)
        : this(engine, new MemoryStream(data), options)
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
        var samplesToRead = Math.Min(buffer.Length, _data.Length - _samplePosition);
        if (samplesToRead <= 0)
        {
            EndOfStreamReached?.Invoke(this, EventArgs.Empty);
            return 0;
        }
        
        _data.AsSpan(_samplePosition, samplesToRead).CopyTo(buffer);
        _samplePosition += samplesToRead;
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
        var length = decoder.Length > 0 || FormatInfo == null
            ? decoder.Length 
            : (int)(FormatInfo.Duration.TotalSeconds * FormatInfo.SampleRate * FormatInfo.ChannelCount);

        return length > 0 ? DecodeKnownLength(decoder, length) : DecodeUnknownLength(decoder);
    }

    private static float[] DecodeKnownLength(ISoundDecoder decoder, int length)
    {
        var samples = new float[length];
        var read = decoder.Decode(samples);
        if (read < length)
        {
            // If fewer samples were read than expected, resize the array to the actual count.
            Array.Resize(ref samples, read);
        }
        return samples;
    }

    private static float[] DecodeUnknownLength(ISoundDecoder decoder)
    {
        const int blockSize = 22050; // Approx 0.5s at 44.1kHz stereo
        var blocks = new List<float[]>();
        var totalSamples = 0;
        
        while(true)
        {
            var block = new float[blockSize * decoder.Channels];
            var samplesRead = decoder.Decode(block);
            if (samplesRead == 0) break;

            if (samplesRead < block.Length)
            {
                Array.Resize(ref block, samplesRead);
            }
            blocks.Add(block);
            totalSamples += samplesRead;
        }

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
        IsDisposed = true;
    }
}