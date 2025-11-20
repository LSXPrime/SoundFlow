using System.Buffers.Binary;
using System.Text;
using SoundFlow.Metadata.Models;

namespace SoundFlow.Metadata.Writers.Tags;

/// <summary>
/// A helper class to construct an 'ilst' atom tree for M4A/MP4 files.
/// </summary>
internal static class M4aTagBuilder
{
    public static M4aAtom BuildIlstAtom(SoundTags tags)
    {
        var ilst = new M4aAtom("ilst");
        
        AddTextAtom(ilst, "©nam", tags.Title);
        AddTextAtom(ilst, "©ART", tags.Artist);
        AddTextAtom(ilst, "©alb", tags.Album);
        AddTextAtom(ilst, "©gen", tags.Genre);
        AddTextAtom(ilst, "©day", tags.Year?.ToString("D4"));
        AddTextAtom(ilst, "©lyr", tags.Lyrics);

        if (tags.TrackNumber.HasValue)
        {
            // Track number format: 2 null bytes, track number (2 bytes), total tracks (2 bytes), 2 null bytes for disk info
            var trknBytes = new byte[8];
            BinaryPrimitives.WriteUInt16BigEndian(trknBytes.AsSpan(2), (ushort)tags.TrackNumber.Value);
            AddDataAtom(ilst, "trkn", 0, trknBytes); // Type 0 = custom bytes
        }
        
        if (tags.AlbumArt != null)
        {
            // Type 13 = JPEG, 14 = PNG. Assume JPEG if not specified.
            AddDataAtom(ilst, "covr", 13, tags.AlbumArt); 
        }

        return ilst;
    }

    private static void AddTextAtom(M4aAtom parent, string type, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var textBytes = Encoding.UTF8.GetBytes(value);
        AddDataAtom(parent, type, 1, textBytes); // Type 1 = UTF-8 text
    }
    
    private static void AddDataAtom(M4aAtom parent, string type, uint dataType, byte[] payload)
    {
        var atom = new M4aAtom(type);
        var dataAtom = new M4aAtom("data");

        // A 'data' atom payload is: 4-byte type/flags, 4-byte null locale, then the data.
        var dataPayload = new byte[8 + payload.Length];
        // The type (e.g., 1 for text, 13 for jpeg) is part of the 4-byte version/flags field.
        BinaryPrimitives.WriteUInt32BigEndian(dataPayload.AsSpan(0), dataType);
        // The next 4 bytes are for locale, typically all null.
        Buffer.BlockCopy(payload, 0, dataPayload, 8, payload.Length);
        
        dataAtom.Data = dataPayload;
        atom.Children.Add(dataAtom);
        parent.Children.Add(atom);
    }
}