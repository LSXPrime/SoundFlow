using System.Text;

namespace SoundFlow.Metadata.Utilities;

/// <summary>
/// A BinaryWriter that writes in Big Endian format, necessary for certain format specifications like FLAC metadata blocks.
/// </summary>
internal class BigEndianBinaryWriter(Stream stream) : BinaryWriter(stream, Encoding.UTF8, true)
{
    public override void Write(short value)
    {
        var bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        base.Write(bytes);
    }
    
    public override void Write(int value)
    {
        var bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        base.Write(bytes);
    }
    
    public override void Write(long value)
    {
        var bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        base.Write(bytes);
    }

    public override void Write(ushort value)
    {
        var bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        base.Write(bytes);
    }

    public override void Write(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        base.Write(bytes);
    }

    public override void Write(ulong value)
    {
        var bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        base.Write(bytes);
    }
    
    /// <summary>
    /// Writes a variable-length quantity to the stream, used for MIDI delta-times and lengths.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteVariableLengthQuantity(long value)
    {
        var buffer = new byte[8];
        var pos = 0;
        
        // Start with the least significant 7 bits
        buffer[pos++] = (byte)(value & 0x7F);
        value >>= 7;

        while (value > 0)
        {
            buffer[pos++] = (byte)((value & 0x7F) | 0x80); // Set the continuation bit
            value >>= 7;
        }

        // The bytes are generated in reverse order, so write them out backwards
        for (var i = pos - 1; i >= 0; i--)
        {
            Write(buffer[i]);
        }
    }
}