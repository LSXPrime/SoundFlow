using System.Runtime.CompilerServices;
using SoundFlow.Enums;

namespace SoundFlow.Utils;

/// <summary>
/// Provides static methods for converting audio buffers between the internal 32-bit float format
/// and various device-specific PCM formats.
/// </summary>
public static class DeviceBufferHelper
{
    /// <summary>
    /// Dispatches conversion from a float buffer to the appropriate device format.
    /// </summary>
    public static void ConvertToDeviceFormat(Span<float> source, nint destination, int length, SampleFormat format)
    {
        switch (format)
        {
            case SampleFormat.S16:
                ConvertFloatTo<short>(source, destination, length);
                break;
            case SampleFormat.S32: 
                ConvertFloatTo<int>(source, destination, length);
                break;
            case SampleFormat.U8: 
                ConvertFloatTo<byte>(source, destination, length);
                break;
            case SampleFormat.S24: 
                ConvertFloatToS24(source, destination, length);
                break;
            case SampleFormat.F32: 
                break; // F32 is the source format, no conversion needed.
            default: throw new NotSupportedException($"Sample format {format} is not supported for output conversion.");
        }
    }

    /// <summary>
    /// Dispatches conversion from a raw device buffer to a float buffer.
    /// </summary>
    public static void ConvertFromDeviceFormat(nint source, Span<float> destination, int length, SampleFormat format)
    {
        switch (format)
        {
            case SampleFormat.S16: 
                ConvertFrom<short>(source, destination, length);
                break;
            case SampleFormat.S32: 
                ConvertFrom<int>(source, destination, length);
                break;
            case SampleFormat.U8: 
                ConvertFrom<byte>(source, destination, length);
                break;
            case SampleFormat.S24: 
                ConvertFromS24(source, destination, length);
                break;
            case SampleFormat.F32:
                // For F32, we just copy the data.
                Extensions.GetSpan<float>(source, length).CopyTo(destination);
                break;
            default: throw new NotSupportedException($"Sample format {format} is not supported for input conversion.");
        }
    }

    #region Generic Conversion Methods

    /// <summary>
    /// Converts a buffer of float samples to a specified integer PCM format and writes to a native memory location.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConvertFloatTo<T>(Span<float> floatBuffer, nint output, int length) where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            var byteSpan = Extensions.GetSpan<byte>(output, length);
            for (var i = 0; i < length; i++)
            {
                var clipped = Math.Clamp(floatBuffer[i], -1f, 1f);
                byteSpan[i] = (byte)((clipped * 127.5f) + 127.5f); // Scale [-1,1] to [0,255]
            }
        }
        else if (typeof(T) == typeof(short))
        {
            var shortSpan = Extensions.GetSpan<short>(output, length);
            for (var i = 0; i < length; i++)
            {
                var clipped = Math.Clamp(floatBuffer[i], -1f, 1f);
                shortSpan[i] = (short)(clipped * short.MaxValue);
            }
        }
        else if (typeof(T) == typeof(int))
        {
            var intSpan = Extensions.GetSpan<int>(output, length);
            const double scale = int.MaxValue;
            for (var i = 0; i < length; i++)
            {
                var clipped = Math.Clamp(floatBuffer[i], -1f, 1f);
                intSpan[i] = (int)(clipped * scale);
            }
        }
        else
        {
            throw new NotSupportedException($"Unsupported output format: {typeof(T)}");
        }

        floatBuffer.Clear();
    }

    /// <summary>
    /// Converts a native buffer from a specified integer PCM format to a buffer of float samples.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConvertFrom<T>(nint input, Span<float> floatBuffer, int length) where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            var byteSpan = Extensions.GetSpan<byte>(input, length);
            const float scale = 1f / 128f;
            for (var i = 0; i < length; i++)
            {
                int originalSample = byteSpan[i];
                var dither = ((float)Random.Shared.NextDouble() - (float)Random.Shared.NextDouble());
                var ditheredSample = originalSample != 0 ? originalSample + dither : originalSample;
                floatBuffer[i] = (ditheredSample - 128f) * scale;
            }
        }
        else if (typeof(T) == typeof(short))
        {
            var shortSpan = Extensions.GetSpan<short>(input, length);
            const float scale = 1f / 32767f;
            for (var i = 0; i < length; i++)
            {
                floatBuffer[i] = shortSpan[i] * scale;
            }
        }
        else if (typeof(T) == typeof(int))
        {
            var intSpan = Extensions.GetSpan<int>(input, length);
            const double scale = 1.0 / 2147483647.0;
            for (var i = 0; i < length; i++)
            {
                floatBuffer[i] = (float)(intSpan[i] * scale);
            }
        }
        else
        {
            throw new NotSupportedException($"Unsupported input format: {typeof(T)}");
        }
    }

    /// <summary>
    /// Converts a buffer of float samples to 24-bit PCM format, packing them into a native byte buffer.
    /// </summary>
    private static void ConvertFloatToS24(Span<float> floatBuffer, nint output, int length)
    {
        var outputSpan = Extensions.GetSpan<byte>(output, length * 3);
        for (int i = 0, j = 0; i < length; i++, j += 3)
        {
            var clipped = Math.Clamp(floatBuffer[i], -1f, 1f);
            var sample24 = (int)(clipped * 8388607);

            outputSpan[j] = (byte)sample24;
            outputSpan[j + 1] = (byte)(sample24 >> 8);
            outputSpan[j + 2] = (byte)(sample24 >> 16);
        }

        floatBuffer.Clear();
    }

    /// <summary>
    /// Converts a native 24-bit PCM byte buffer to a buffer of float samples.
    /// </summary>
    private static void ConvertFromS24(nint input, Span<float> floatBuffer, int length)
    {
        var inputSpan = Extensions.GetSpan<byte>(input, length * 3);
        const float scale = 1f / 8388607f;
        for (int i = 0, j = 0; i < length; i++, j += 3)
        {
            // Reconstruct the 24-bit sample from three bytes (little-endian)
            var sample24 = (inputSpan[j]) | (inputSpan[j + 1] << 8) | (inputSpan[j + 2] << 16);

            // Sign-extend if the 24th bit is set
            if ((sample24 & 0x800000) != 0)
                sample24 |= unchecked((int)0xFF000000);

            floatBuffer[i] = sample24 * scale;
        }
    }

    #endregion
}