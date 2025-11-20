namespace SoundFlow.Metadata.Utilities;

/// <summary>
/// A utility class to calculate the CRC32 checksum required for Ogg pages.
/// This implementation uses a standard lookup table for performance.
/// </summary>
internal static class OggCrc
{
    private const uint Polynomial = 0x04C11DB7;
    private static readonly uint[] CrcTable = new uint[256];

    static OggCrc()
    {
        for (uint i = 0; i < 256; i++)
        {
            var crc = i << 24;
            for (var j = 0; j < 8; j++)
            {
                crc = (crc << 1) ^ ((crc & 0x80000000) != 0 ? Polynomial : 0);
            }
            CrcTable[i] = crc;
        }
    }

    /// <summary>
    /// Calculates the CRC32 checksum for a given byte array segment.
    /// </summary>
    public static uint Calculate(byte[] data, int offset, int length)
    {
        uint crc = 0;
        for (var i = offset; i < offset + length; i++)
        {
            crc = (crc << 8) ^ CrcTable[(crc >> 24) ^ data[i]];
        }
        return crc;
    }
}