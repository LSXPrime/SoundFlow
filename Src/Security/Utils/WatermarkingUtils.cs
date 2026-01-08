using System.Runtime.InteropServices;

namespace SoundFlow.Security.Utils;

/// <summary>
/// Provides utility methods for Pearson hashing.
/// </summary>
public static class WatermarkingUtils
{
    /// <summary>
    /// The Pearson hashing permutation table (0-255).
    /// </summary>
    public static readonly byte[] PearsonTable =
    [
        251, 175, 119, 215, 81, 14, 79, 191, 103, 49, 181, 143, 186, 157, 0, 232,
        31, 239, 229, 55, 129, 28, 99, 69, 23, 165, 32, 145, 20, 87, 24, 96,
        253, 169, 109, 223, 50, 67, 130, 92, 152, 36, 208, 230, 206, 196, 71, 252,
        64, 91, 45, 190, 85, 12, 106, 240, 111, 211, 197, 101, 154, 53, 209, 217,
        112, 29, 247, 48, 249, 133, 113, 203, 238, 201, 227, 214, 136, 108, 16, 128,
        192, 156, 193, 218, 177, 245, 84, 6, 19, 107, 195, 167, 1, 95, 62, 52,
        187, 33, 116, 56, 13, 10, 221, 222, 125, 42, 17, 189, 58, 207, 144, 254,
        155, 199, 172, 162, 148, 117, 185, 118, 140, 124, 25, 171, 90, 233, 228, 131,
        122, 188, 77, 163, 153, 37, 237, 242, 3, 15, 246, 26, 134, 183, 158, 66,
        231, 150, 147, 86, 216, 220, 102, 224, 164, 204, 30, 126, 11, 22, 135, 100,
        57, 115, 93, 120, 159, 132, 114, 21, 210, 123, 72, 59, 243, 27, 7, 8,
        40, 236, 68, 73, 63, 198, 225, 76, 255, 41, 38, 18, 88, 65, 105, 139,
        9, 127, 226, 78, 160, 5, 235, 46, 74, 39, 2, 248, 142, 205, 47, 241,
        146, 180, 250, 149, 138, 212, 121, 166, 104, 89, 137, 194, 219, 70, 244, 184,
        60, 4, 170, 213, 176, 80, 234, 173, 168, 200, 178, 97, 141, 94, 75, 43,
        83, 35, 161, 202, 110, 215, 174, 82, 34, 179, 151, 44, 98, 182, 51, 54
    ];
    
    /// <summary>
    /// Calculates the 8-bit Pearson hash of the float buffer.
    /// Pearson hashing provides a good distribution for data integrity checks with low collision rates for small changes.
    /// </summary>
    public static byte CalculatePearsonHash(Span<float> data)
    {
        byte h = 0;
        var byteData = MemoryMarshal.Cast<float, byte>(data);

        // Process every byte of the float array
        foreach (var t in byteData)
        {
            h = PearsonTable[h ^ t];
        }

        return h;
    }

    /// <summary>
    /// Calculates the next float in a sequence using XorShift32.
    /// </summary>
    /// <param name="rngState">The current state of the PRNG.</param>
    /// <returns>The next float in the sequence and the updated state.</returns>
    public static (float NextFloat, uint CurrentState) NextFloat(uint rngState)
    {
        // XorShift32
        var x = rngState;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        rngState = x;

        // Normalize to [0, 1]
        return ((float)x / uint.MaxValue, rngState);
    }

    /// <summary>
    /// Calculates the FNV-1a hash of a string.
    /// FNV-1a is a fast non-cryptographic hash algorithm with good distribution for stable seeding.
    /// </summary>
    /// <param name="str">The string to hash.</param>
    /// <returns>The FNV-1a hash of the string.</returns>
    public static uint GetStableHash(string str)
    {
        var hash = 2166136261;
        foreach (var c in str)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return hash;
    }
}