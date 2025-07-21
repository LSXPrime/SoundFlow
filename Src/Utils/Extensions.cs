using System.Runtime.InteropServices;
using SoundFlow.Enums;

namespace SoundFlow.Utils;

/// <summary>
///     Extension methods.
/// </summary>
public static class Extensions
{
    /// <summary>
    ///     Gets the size of a single sample in bytes for this sample format.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="sampleFormat" /> is invalid.</exception>
    /// <returns>The size of a single sample in bytes.</returns>
    public static int GetBytesPerSample(this SampleFormat sampleFormat)
    {
        return sampleFormat switch
        {
            SampleFormat.U8 => 1,
            SampleFormat.S16 => 2,
            SampleFormat.S24 => 3,
            SampleFormat.S32 => 4,
            SampleFormat.F32 => 4,
            SampleFormat.Unknown => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(sampleFormat), "Invalid SampleFormat")
        };
    }

    /// <summary>
    ///     Gets a <see cref="Span{T}" /> for a given pointer and length.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the span.</typeparam>
    /// <param name="ptr">The pointer to the first element of the span.</param>
    /// <param name="length">The number of elements in the span.</param>
    /// <returns>A <see cref="Span{T}" /> for the given pointer and length.</returns>
    public static unsafe Span<T> GetSpan<T>(nint ptr, int length) where T : unmanaged
    {
        return new Span<T>((void*)ptr, length);
    }

    /// <summary>
    ///     Reads an array of structures from a native memory pointer.
    /// </summary>
    /// <typeparam name="T">The type of the structures to read. Must be a value type.</typeparam>
    /// <param name="pointer">The native pointer to the start of the array.</param>
    /// <param name="count">The number of structures to read.</param>
    /// <returns>An array of structures of type <typeparamref name="T"/> read from the specified pointer.</returns>
    public static T[] ReadArray<T>(this nint pointer, int count) where T : struct
    {
        var array = new T[count];
        for (var i = 0; i < count; i++)
        {
            var currentPtr = (nint)((long)pointer + i * Marshal.SizeOf<T>());
            array[i] = Marshal.PtrToStructure<T>(currentPtr);
        }

        return array;
    }
}
