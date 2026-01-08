using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Utils;
using System.Buffers;
using System.Runtime.InteropServices;
using SoundFlow.Metadata.Models;

namespace SoundFlow.Providers;

/// <summary>
///     Provides audio data from a raw PCM stream or a raw float array.
///     This provider is designed for sources that directly contain raw PCM bytes or float samples without any encoding headers.
/// </summary>
public class RawDataProvider : ISoundDataProvider
{
    private readonly Stream? _pcmStream;
    private readonly float[]? _floatData;
    private readonly byte[]? _byteArray;
    private readonly int[]? _intArray;
    private readonly short[]? _shortData;
    private readonly SampleFormat _sampleFormat;
    private int _position;
    
    /// <summary>
    ///     Initializes a new instance of the <see cref="RawDataProvider"/> class from a raw float array.
    /// </summary>
    /// <param name="rawSamples">The raw float array containing the audio samples.</param>
    /// <param name="sampleRate">The sample rate of the audio data. Defaults to 48000.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="rawSamples"/> is <c>null</c>.</exception>
    public RawDataProvider(float[] rawSamples, int sampleRate = 48000)
    {
        _floatData = rawSamples ?? throw new ArgumentNullException(nameof(rawSamples));
        _sampleFormat = SampleFormat.F32;
        SampleRate = sampleRate;
    }
    
    /// <summary>
    ///     Initializes a new instance of the <see cref="RawDataProvider"/> class from a raw PCM stream.
    /// </summary>
    /// <param name="pcmStream">The raw PCM stream containing the audio samples.</param>
    /// <param name="sampleFormat">The sample format of the raw PCM stream.</param>
    /// <param name="sampleRate">The sample rate of the audio data. Defaults to 48000.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="pcmStream"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="sampleFormat"/> is <see cref="SampleFormat.Unknown"/>.</exception>
    public RawDataProvider(Stream pcmStream, SampleFormat sampleFormat, int sampleRate = 48000)
    {
        _pcmStream = pcmStream ?? throw new ArgumentNullException(nameof(pcmStream));
        _sampleFormat = sampleFormat != SampleFormat.Unknown ? sampleFormat 
            : throw new ArgumentException("SampleFormat cannot be Unknown for RawDataProvider when using a stream.", nameof(sampleFormat));
        SampleRate = sampleRate;
    }
    
    /// <summary>
    ///     Initializes a new instance of the <see cref="RawDataProvider"/> class from a raw byte array.
    /// </summary>
    /// <param name="rawBytes">The raw byte array containing the audio samples.</param>
    /// <param name="sampleFormat">The sample format of the raw byte array.</param>
    /// <param name="sampleRate">The sample rate of the audio data. Defaults to 48000.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="rawBytes"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="sampleFormat"/> is <see cref="SampleFormat.Unknown"/>.</exception>
    public RawDataProvider(byte[] rawBytes, SampleFormat sampleFormat, int sampleRate = 48000)
    {
        _byteArray = rawBytes ?? throw new ArgumentNullException(nameof(rawBytes));
        _sampleFormat = sampleFormat != SampleFormat.Unknown ? sampleFormat 
            : throw new ArgumentException("SampleFormat cannot be Unknown for RawDataProvider when using a byte array.", nameof(sampleFormat));
        SampleRate = sampleRate;
    }
    
    /// <summary>
    ///     Initializes a new instance of the <see cref="RawDataProvider"/> class from a raw int array.
    /// </summary>
    /// <param name="rawSamples">The raw int array containing the audio samples.</param>
    /// <param name="sampleRate">The sample rate of the audio data. Defaults to 48000.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="rawSamples"/> is <c>null</c>.</exception>
    public RawDataProvider(int[] rawSamples, int sampleRate = 48000)
    {
        _intArray = rawSamples ?? throw new ArgumentNullException(nameof(rawSamples));
        _sampleFormat = SampleFormat.S32;
        SampleRate = sampleRate;
    }
    
    /// <summary>
    ///     Initializes a new instance of the <see cref="RawDataProvider"/> class from a raw short array.
    /// </summary>
    /// <param name="rawSamples">The raw short array containing the audio samples.</param>
    /// <param name="sampleRate">The sample rate of the audio data. Defaults to 48000.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="rawSamples"/> is <c>null</c>.</exception>
    public RawDataProvider(short[] rawSamples, int sampleRate = 48000)
    {
        _shortData = rawSamples ?? throw new ArgumentNullException(nameof(rawSamples));
        _sampleFormat = SampleFormat.S16;
        SampleRate = sampleRate;
    }
    

    /// <inheritdoc />
    public int Position => _position;

    /// <inheritdoc />
    public int Length => GetLength();

    /// <inheritdoc />
    public bool CanSeek => _pcmStream?.CanSeek ?? true;

    /// <inheritdoc />
    public SampleFormat SampleFormat => _sampleFormat;

    /// <inheritdoc />
    public int SampleRate { get; init; } = 48000;

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }
    
    /// <inheritdoc />
    public SoundFormatInfo? FormatInfo { get; init; }

    /// <inheritdoc />
    public event EventHandler<EventArgs>? EndOfStreamReached;

    /// <inheritdoc />
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">Thrown if the provider has been disposed.</exception>
    public int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed) return 0;

        var samplesActuallyRead = ReadData(buffer);

        if (samplesActuallyRead == 0)
        {
            EndOfStreamReached?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        _position += samplesActuallyRead;
        PositionChanged?.Invoke(this, new PositionChangedEventArgs(_position));
        return samplesActuallyRead;
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">Thrown if the provider has been disposed.</exception>
    /// <exception cref="NotSupportedException">Thrown if seeking is not supported on the underlying PCM stream.</exception>
    /// <exception cref="InvalidOperationException">Thrown if no data source is initialized.</exception>
    public void Seek(int sampleOffset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (_pcmStream != null)
        {
            if (!_pcmStream.CanSeek)
                throw new NotSupportedException("Seeking is not supported for the underlying PCM stream.");

            sampleOffset = ClampSampleOffset(sampleOffset, _pcmStream.Length / _sampleFormat.GetBytesPerSample());
            _pcmStream.Seek((long)sampleOffset * _sampleFormat.GetBytesPerSample(), SeekOrigin.Begin);
        }
        else
        {
            sampleOffset = ClampSampleOffset(sampleOffset, GetArrayLength());
        }

        _position = sampleOffset;
        PositionChanged?.Invoke(this, new PositionChangedEventArgs(_position));
    }

    /// <summary>
    ///     Releases the unmanaged resources used by the <see cref="RawDataProvider"/>.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        
        _pcmStream?.Dispose();
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    #region Helper Methods

    /// <summary>
    ///     Calculates the total length of the audio data in samples
    /// </summary>
    /// <returns>
    ///     Length in samples for streams that can seek, -1 for non-seekable streams,
    ///     or length of array data sources
    /// </returns>
    private int GetLength()
    {
        if (_pcmStream != null)
        {
            return _pcmStream.CanSeek ? (int)(_pcmStream.Length / _sampleFormat.GetBytesPerSample()) : -1;
        }
        
        return GetArrayLength();
    }

    /// <summary>
    ///     Gets the length of array-based data sources in samples
    /// </summary>
    /// <returns>Number of samples in the current array source</returns>
    private int GetArrayLength()
    {
        return _floatData?.Length ?? 
               _byteArray?.Length / _sampleFormat.GetBytesPerSample() ?? 
               _intArray?.Length ?? 
               _shortData?.Length ?? 0;
    }

    /// <summary>
    ///     Reads audio data into the provided buffer from the current source
    /// </summary>
    /// <param name="buffer">Target buffer for the audio samples</param>
    /// <returns>Number of samples actually read</returns>
    private int ReadData(Span<float> buffer)
    {
        if (_pcmStream != null)
            return ReadFromStream(buffer);
        if (_floatData != null)
            return ReadFromArray(_floatData, buffer, srcSample => srcSample);
        if (_intArray != null)
            return ReadFromArray(_intArray, buffer, srcSample => srcSample / (float)int.MaxValue);
        if (_shortData != null)
            return ReadFromArray(_shortData, buffer, srcSample => srcSample / (float)short.MaxValue);
        if (_byteArray != null)
        {
            var bytesPerSample = _sampleFormat.GetBytesPerSample();
            var byteOffset = _position * bytesPerSample;
            var bytesToRead = Math.Min(buffer.Length * bytesPerSample, _byteArray.Length - byteOffset);
            
            if (bytesToRead <= 0) return 0;
            
            ConvertBytesToFloat(_byteArray.AsSpan(byteOffset, bytesToRead), buffer[..(bytesToRead / bytesPerSample)], _sampleFormat);
            return bytesToRead / bytesPerSample;
        }
        
        return 0;
    }

    /// <summary>
    ///     Reads audio data from a stream source
    /// </summary>
    /// <param name="buffer">Target buffer for the audio samples</param>
    /// <returns>Number of samples actually read</returns>
    private int ReadFromStream(Span<float> buffer)
    {
        var bytesPerSample = _sampleFormat.GetBytesPerSample();
        var bytesToRead = buffer.Length * bytesPerSample;
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(bytesToRead);
        
        try
        {
            var byteBuffer = rentedBuffer.AsSpan(0, bytesToRead);
            var bytesActuallyRead = _pcmStream!.Read(byteBuffer);
            
            if (bytesActuallyRead == 0) return 0;
            
            var samplesActuallyRead = bytesActuallyRead / bytesPerSample;
            ConvertBytesToFloat(byteBuffer[..bytesActuallyRead], buffer[..samplesActuallyRead], _sampleFormat);
            return samplesActuallyRead;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    /// <summary>
    ///     Generic method to read from array-based sources
    /// </summary>
    /// <typeparam name="T">Type of the source array</typeparam>
    /// <param name="source">Source array containing audio data</param>
    /// <param name="buffer">Target buffer for the audio samples</param>
    /// <param name="convertAction">Conversion action to transform a single source sample to float</param>
    /// <returns>Number of samples actually read</returns>
    private int ReadFromArray<T>(T[] source, Span<float> buffer, Func<T, float> convertAction)
    {
        var remainingSamples = source.Length - _position;
        var samplesActuallyRead = Math.Min(buffer.Length, remainingSamples);
        
        if (samplesActuallyRead <= 0) return 0;
        
        for (var i = 0; i < samplesActuallyRead; i++)
        {
            buffer[i] = convertAction(source[_position + i]);
        }
        
        return samplesActuallyRead;
    }

    /// <summary>
    ///     Clamps the sample offset to valid range
    /// </summary>
    /// <param name="offset">Requested sample offset</param>
    /// <param name="maxSamples">Maximum available samples</param>
    /// <returns>Clamped sample offset within valid range</returns>
    private static int ClampSampleOffset(int offset, long maxSamples)
    {
        return (int)Math.Clamp(offset, 0, maxSamples);
    }

    /// <summary>
    ///     Converts raw audio bytes to normalized float samples
    /// </summary>
    /// <param name="byteBuffer">Source byte buffer</param>
    /// <param name="floatBuffer">Target float buffer</param>
    /// <param name="format">Source sample format</param>
    /// <exception cref="NotSupportedException">Thrown for unsupported sample formats</exception>
    private static void ConvertBytesToFloat(Span<byte> byteBuffer, Span<float> floatBuffer, SampleFormat format)
    {
        var sampleCount = floatBuffer.Length;

        switch (format)
        {
            case SampleFormat.U8:
                // Convert unsigned 8-bit (0-255) to float (-1.0 to 1.0)
                for (var i = 0; i < sampleCount; i++)
                    floatBuffer[i] = i < byteBuffer.Length ? (byteBuffer[i] - 128) / 128f : 0;
                break;
            case SampleFormat.S16:
                // Convert signed 16-bit to float
                var shortSpan = MemoryMarshal.Cast<byte, short>(byteBuffer);
                for (var i = 0; i < sampleCount; i++)
                    floatBuffer[i] = shortSpan[i] / (float)short.MaxValue;
                break;
            case SampleFormat.S24:
                // Convert signed 24-bit to float
                for (var i = 0; i < sampleCount; i++)
                {
                    var byteIndex = i * 3;
                    floatBuffer[i] = byteIndex + 2 < byteBuffer.Length ? 
                        Convert24BitToFloat(byteBuffer, byteIndex) : 0;
                }
                break;
            case SampleFormat.S32:
                // Convert signed 32-bit to float
                var int32Span = MemoryMarshal.Cast<byte, int>(byteBuffer);
                for (var i = 0; i < sampleCount; i++)
                    floatBuffer[i] = int32Span[i] / (float)int.MaxValue;
                break;
            case SampleFormat.F32:
                // Direct copy for float samples
                MemoryMarshal.Cast<byte, float>(byteBuffer).CopyTo(floatBuffer);
                break;
            default:
                throw new NotSupportedException($"Sample format {format} is not supported.");
        }
    }

    /// <summary>
    ///     Converts 24-bit packed samples to float
    /// </summary>
    /// <param name="bytes">Source byte buffer</param>
    /// <param name="index">Starting index of 24-bit sample</param>
    /// <returns>Normalized float sample</returns>
    private static float Convert24BitToFloat(Span<byte> bytes, int index)
    {
        var sample24 = (bytes[index] << 0) | (bytes[index + 1] << 8) | (bytes[index + 2] << 16);
        if ((sample24 & 0x800000) != 0) // Check sign bit
            sample24 |= unchecked((int)0xFF000000); // Sign extend
        return sample24 / 8388608f; // Normalize (2^23)
    }

    #endregion
}