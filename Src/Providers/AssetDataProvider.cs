using System;
using System.Collections.Generic;
using System.IO;
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
    private float[]? _data;
    private int _samplePosition;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AssetDataProvider" /> class by reading from a file path.
    ///     This method handles the stream lifecycle internally, ensuring the file handle is closed immediately after reading.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="filePath">The absolute or relative path to the audio file.</param>
    /// <param name="options">Optional configuration for metadata reading.</param>
    public AssetDataProvider(AudioEngine engine, string filePath, ReadOptions? options = null)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Initialize(engine, stream, options ?? new ReadOptions(), null);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AssetDataProvider" /> class by reading from a stream and detecting its format.
    ///     If metadata reading fails, it will attempt to probe the stream with registered codecs.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="stream">The stream to read audio data from.</param>
    /// <param name="options">Optional configuration for metadata reading.</param>
    public AssetDataProvider(AudioEngine engine, Stream stream, ReadOptions? options = null)
    {
        Initialize(engine, stream, options ?? new ReadOptions(), null);
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
        var options = new ReadOptions
        {
            ReadTags = false,
            ReadAlbumArt = false,
            DurationAccuracy = DurationAccuracy.FastEstimate
        };
        Initialize(engine, stream, options, format);
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

    private void Initialize(AudioEngine engine, Stream stream, ReadOptions options, AudioFormat? explicitFormat)
    {
        var formatInfoResult = SoundMetadataReader.Read(stream, options);
        ISoundDecoder decoder;

        // Reset stream position before decoding attempts
        stream.Position = 0;

        if (formatInfoResult is { IsSuccess: true, Value: not null })
        {
            FormatInfo = formatInfoResult.Value;
            
            // If explicit format is provided, use it; otherwise, derive from metadata
            var targetFormat = explicitFormat ?? new AudioFormat
            {
                Format = SampleFormat.F32,
                Channels = FormatInfo.ChannelCount,
                Layout = AudioFormat.GetLayoutFromChannels(FormatInfo.ChannelCount),
                SampleRate = FormatInfo.SampleRate
            };

            decoder = engine.CreateDecoder(stream, FormatInfo.FormatIdentifier, targetFormat);
        }
        else
        {
            // Fallback to probing
            decoder = explicitFormat.HasValue ? engine.CreateDecoder(stream, out _, explicitFormat.Value) : engine.CreateDecoder(stream, out _);

            FormatInfo = new SoundFormatInfo
            {
                FormatName = "Unknown (Probed)",
                FormatIdentifier = "unknown",
                ChannelCount = explicitFormat?.Channels ?? 0, // Fallback if available, or 0 (decoder usually provides valid info)
                SampleRate = explicitFormat?.SampleRate ?? 0,
                Duration = TimeSpan.Zero
            };
            
            // Refine FormatInfo based on actual decoder properties if probe succeeded
            if (decoder is { Channels: > 0, SampleRate: > 0 })
            {
                FormatInfo = FormatInfo with
                {
                    ChannelCount = decoder.Channels,
                    SampleRate = decoder.SampleRate,
                    Duration = decoder.Length > 0
                        ? TimeSpan.FromSeconds((double)decoder.Length / (decoder.SampleRate * decoder.Channels))
                        : TimeSpan.Zero
                };
            }
        }

        try
        {
            _data = Decode(decoder);
            SampleRate = explicitFormat?.SampleRate ?? FormatInfo.SampleRate;
            Length = _data.Length;
        }
        finally
        {
            decoder.Dispose();
        }
    }

    /// <inheritdoc />
    public int Position => _samplePosition;

    /// <inheritdoc />
    public int Length { get; private set; } // Length in samples

    /// <inheritdoc />
    public bool CanSeek => true;

    /// <inheritdoc />
    public SampleFormat SampleFormat { get; private set; }

    /// <inheritdoc />
    public int SampleRate { get; private set; }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public SoundFormatInfo? FormatInfo { get; private set; }

    /// <inheritdoc />
    public event EventHandler<EventArgs>? EndOfStreamReached;

    /// <inheritdoc />
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    /// <inheritdoc />
    public int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed || _data is null) return 0;

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
        if (IsDisposed || _data is null) return;

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

        while (true)
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
        _data = null;
        EndOfStreamReached = null;
        PositionChanged = null;
    }
}