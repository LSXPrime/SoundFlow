using System.Collections;
using System.Text;

namespace SoundFlow.Security.Payloads;

/// <summary>
/// A watermark payload representing a text string.
/// </summary>
/// <param name="text">The text to embed.</param>
public class TextPayload(string text) : IWatermarkPayload
{
    /// <summary>
    /// Gets the text content of the payload.
    /// </summary>
    public string Text { get; } = text;

    /// <summary>
    /// Helper constructor for deserialization or empty initialization.
    /// </summary>
    public TextPayload() : this(string.Empty) { }

    /// <inheritdoc />
    public BitArray ToBits()
    {
        var bytes = Encoding.UTF8.GetBytes(Text);
        // Prefix with length (4 bytes integer)
        var lengthBytes = BitConverter.GetBytes(bytes.Length);
        var combined = new byte[lengthBytes.Length + bytes.Length];
        
        Buffer.BlockCopy(lengthBytes, 0, combined, 0, lengthBytes.Length);
        Buffer.BlockCopy(bytes, 0, combined, lengthBytes.Length, bytes.Length);

        return new BitArray(combined);
    }

    /// <inheritdoc />
    public object FromBits(BitArray bits)
    {
        var bytes = new byte[(bits.Length + 7) / 8];
        bits.CopyTo(bytes, 0);

        if (bytes.Length < 4) return string.Empty;

        var length = BitConverter.ToInt32(bytes, 0);
        if (length < 0 || length > bytes.Length - 4) return string.Empty;

        return Encoding.UTF8.GetString(bytes, 4, length);
    }
}