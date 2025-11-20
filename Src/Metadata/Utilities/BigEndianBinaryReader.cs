using System.Text;

namespace SoundFlow.Metadata.Utilities;

/// <summary>
///     A BinaryReader that reads in Big Endian format, necessary for formats like AIFF.
/// </summary>
internal class BigEndianBinaryReader(Stream stream) : BinaryReader(stream, Encoding.UTF8, true)
{
    public override short ReadInt16()
    {
        return BitConverter.ToInt16(ReadReversedBytes(2), 0);
    }

    public override int ReadInt32()
    {
        return BitConverter.ToInt32(ReadReversedBytes(4), 0);
    }

    public override ushort ReadUInt16()
    {
        return BitConverter.ToUInt16(ReadReversedBytes(2), 0);
    }

    public override uint ReadUInt32()
    {
        return BitConverter.ToUInt32(ReadReversedBytes(4), 0);
    }

    public override long ReadInt64()
    {
        return BitConverter.ToInt64(ReadReversedBytes(8), 0);
    }

    public string ReadString(int count)
    {
        return new string(ReadChars(count));
    }

    /// <summary>
    /// Reads a fixed-length string as raw bytes, suitable for box type identifiers
    /// </summary>
    public string ReadFixedString(int count)
    {
        var bytes = ReadBytes(count);
        return Encoding.GetEncoding("ISO-8859-1").GetString(bytes);
    }

    /// <summary>
    ///     Reads an 80-bit IEEE 754 extended-precision float, common in AIFF files.
    /// </summary>
    public double ReadExtended()
    {
        // Array of 10 bytes in little-endian order.
        var bytes = ReadReversedBytes(10);

        // Reconstruct the 15-bit exponent from the first two original bytes
        var exponentRaw = (ushort)((bytes[9] << 8) | bytes[8]);
    
        // Extract the sign from the most significant bit of the first original byte
        var sign = (bytes[9] & 0x80) != 0;
    
        // Remove the sign bit to get the pure 15-bit exponent
        exponentRaw &= 0x7FFF;

        // The mantissa is in bytes[0] through bytes[7] in perfect little-endian order.
        var mantissa = BitConverter.ToUInt64(bytes, 0);

        // The float value is undefined if the exponent is 0 and the mantissa is 0
        if ((exponentRaw == 0 && mantissa == 0) || exponentRaw == 0x7FFF)
            return 0.0;

        // Unbias the exponent. The bias for 80-bit floats is 16383.
        var exponent = exponentRaw - 16383;

        // The value of a normalized float is: mantissa * 2^(exponent - 63)
        double result = mantissa;
        result *= Math.Pow(2.0, exponent - 63);

        return sign ? -result : result;
    }

    private byte[] ReadReversedBytes(int count)
    {
        var bytes = base.ReadBytes(count);
        if (bytes.Length < count) throw new EndOfStreamException();
        Array.Reverse(bytes);
        return bytes;
    }
}