using System.Text;
using SoundFlow.Metadata.Models;

namespace SoundFlow.Metadata.Writers.Tags;

/// <summary>
/// A helper class to construct an ID3v2 tag block from a SoundTags object.
/// </summary>
internal static class Id3V2Builder
{
    public static byte[] Build(SoundTags tags)
    {
        using var framesStream = new MemoryStream();

        // Write text frames
        WriteTextFrame(framesStream, "TIT2", tags.Title);
        WriteTextFrame(framesStream, "TPE1", tags.Artist);
        WriteTextFrame(framesStream, "TALB", tags.Album);
        WriteTextFrame(framesStream, "TCON", tags.Genre);
        WriteTextFrame(framesStream, "TYER", tags.Year?.ToString("D4"));
        WriteTextFrame(framesStream, "TRCK", tags.TrackNumber?.ToString());
        
        // Write lyrics frame
        if (tags.Lyrics != null) WriteUsltFrame(framesStream, tags.Lyrics);

        // Write picture frame
        if (tags.AlbumArt != null) WriteApicFrame(framesStream, tags.AlbumArt);

        var framesData = framesStream.ToArray();
        var tagSize = framesData.Length;
        var finalTag = new byte[10 + tagSize];

        // Header: "ID3"
        finalTag[0] = 0x49;
        finalTag[1] = 0x44;
        finalTag[2] = 0x33;

        // Version: v2.3.0
        finalTag[3] = 0x03;
        finalTag[4] = 0x00;

        // Flags: 0
        finalTag[5] = 0x00;

        // Size (Synchsafe integer)
        finalTag[6] = (byte)((tagSize >> 21) & 0x7F);
        finalTag[7] = (byte)((tagSize >> 14) & 0x7F);
        finalTag[8] = (byte)((tagSize >> 7) & 0x7F);
        finalTag[9] = (byte)(tagSize & 0x7F);

        // Copy frame data
        Buffer.BlockCopy(framesData, 0, finalTag, 10, tagSize);

        return finalTag;
    }

    private static void WriteTextFrame(Stream stream, string frameId, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;

        // Encoding byte (1 = UTF-16 with BOM) + BOM + text
        var encoding = Encoding.Unicode; // UTF-16 LE
        var bom = encoding.GetPreamble();
        var textBytes = encoding.GetBytes(value);
        var content = new byte[1 + bom.Length + textBytes.Length];
        
        content[0] = 1; // UTF-16 encoding byte
        Buffer.BlockCopy(bom, 0, content, 1, bom.Length);
        Buffer.BlockCopy(textBytes, 0, content, 1 + bom.Length, textBytes.Length);
        
        WriteFrameHeader(stream, frameId, content.Length);
        stream.Write(content);
    }
    
    private static void WriteUsltFrame(Stream stream, string lyrics)
    {
        // Encoding byte (1 = UTF-16 LE) + Language ("eng") + Content Descriptor (BOM) + Lyrics (BOM + Text)
        var encoding = Encoding.Unicode;
        var bom = encoding.GetPreamble();
        var lyricsBytes = encoding.GetBytes(lyrics);

        using var contentStream = new MemoryStream();
        contentStream.WriteByte(1); // Encoding: UTF-16
        contentStream.Write("eng"u8); // Language
        contentStream.Write(bom); // Content descriptor is just a terminator
        contentStream.Write(bom); // BOM for the actual lyrics text
        contentStream.Write(lyricsBytes);
        
        var content = contentStream.ToArray();
        WriteFrameHeader(stream, "USLT", content.Length);
        stream.Write(content);
    }

    private static void WriteApicFrame(Stream stream, byte[] pictureData)
    {
        // For simplicity, we'll assume JPEG and write a standard APIC frame.
        const string mimeType = "image/jpeg";
        
        using var contentStream = new MemoryStream();
        contentStream.WriteByte(0); // Encoding: ISO-8859-1 for mime/desc
        contentStream.Write(Encoding.GetEncoding("ISO-8859-1").GetBytes(mimeType));
        contentStream.WriteByte(0); // Mime terminator
        contentStream.WriteByte(3); // Picture Type: 3 (Cover Front)
        contentStream.WriteByte(0); // Description terminator
        contentStream.Write(pictureData);

        var content = contentStream.ToArray();
        WriteFrameHeader(stream, "APIC", content.Length);
        stream.Write(content);
    }

    private static void WriteFrameHeader(Stream stream, string frameId, int frameSize)
    {
        stream.Write(Encoding.ASCII.GetBytes(frameId));
        stream.WriteByte((byte)((frameSize >> 24) & 0xFF));
        stream.WriteByte((byte)((frameSize >> 16) & 0xFF));
        stream.WriteByte((byte)((frameSize >> 8) & 0xFF));
        stream.WriteByte((byte)(frameSize & 0xFF));
        stream.WriteByte(0); // Flags
        stream.WriteByte(0); // Flags
    }
}