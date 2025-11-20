using System.Text;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Utilities;

namespace SoundFlow.Metadata.Writers.Tags;

/// <summary>
/// A helper class to construct a Vorbis Comment block from a SoundTags object.
/// </summary>
internal static class VorbisCommentBuilder
{
    private const string VendorString = "SoundFlow";

    public static byte[] Build(SoundTags tags)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        var comments = new List<string>();
        if (!string.IsNullOrEmpty(tags.Title)) comments.Add($"TITLE={tags.Title}");
        if (!string.IsNullOrEmpty(tags.Artist)) comments.Add($"ARTIST={tags.Artist}");
        if (!string.IsNullOrEmpty(tags.Album)) comments.Add($"ALBUM={tags.Album}");
        if (!string.IsNullOrEmpty(tags.Genre)) comments.Add($"GENRE={tags.Genre}");
        if (tags.Year.HasValue) comments.Add($"DATE={tags.Year.Value:D4}");
        if (tags.TrackNumber.HasValue) comments.Add($"TRACKNUMBER={tags.TrackNumber.Value}");
        if (!string.IsNullOrEmpty(tags.Lyrics)) comments.Add($"LYRICS={tags.Lyrics}");
        
        // Vendor string
        writer.Write(Encoding.UTF8.GetByteCount(VendorString));
        writer.Write(Encoding.UTF8.GetBytes(VendorString));

        // Number of comments
        writer.Write((uint)comments.Count);

        // Write each comment
        foreach (var comment in comments)
        {
            writer.Write(Encoding.UTF8.GetByteCount(comment));
            writer.Write(Encoding.UTF8.GetBytes(comment));
        }

        return ms.ToArray();
    }
    
    public static byte[]? BuildPictureBlock(SoundTags tags)
    {
        if (tags.AlbumArt == null) return null;

        using var ms = new MemoryStream();
        // Use BigEndian for FLAC picture blocks as per spec
        using var writer = new BigEndianBinaryWriter(ms);

        writer.Write((uint)3); // Picture Type: Cover (front)
        
        const string mime = "image/jpeg";
        writer.Write((uint)mime.Length);
        writer.Write(Encoding.ASCII.GetBytes(mime));
        
        writer.Write((uint)0); // Description length 0
        
        // Picture geometry (not used, all zeros)
        writer.Write(new byte[16]);
        
        writer.Write((uint)tags.AlbumArt.Length);
        writer.Write(tags.AlbumArt);

        return ms.ToArray();
    }
}