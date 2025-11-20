using System.Text;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Utilities;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Readers.Tags;

internal class VorbisCommentReader
{
    public Result<SoundTags> Read(Stream stream, long blockSize, ReadOptions options)
    {
        var tags = new SoundTags();
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            var startPos = stream.Position;

            var vendorLength = reader.ReadInt32();
            if(vendorLength < 0 || stream.Position + vendorLength > startPos + blockSize)
                return new CorruptChunkError("VorbisComment", "Invalid vendor string length.");
            reader.ReadBytes(vendorLength);

            var userCommentCount = reader.ReadUInt32();
            for (var i = 0; i < userCommentCount && stream.Position < startPos + blockSize; i++)
            {
                var commentLength = reader.ReadInt32();
                if (commentLength < 0 || stream.Position + commentLength > startPos + blockSize)
                    return new CorruptChunkError("VorbisComment", "Comment length exceeds block boundaries.");

                var commentBytes = reader.ReadBytes(commentLength);
                var comment = Encoding.UTF8.GetString(commentBytes);
                var parts = comment.Split(['='], 2);
                if (parts.Length != 2) continue;

                var parseResult = ParseComment(parts[0].ToUpperInvariant(), parts[1], tags, options);
                if (parseResult.IsFailure) return Result<SoundTags>.Fail(parseResult.Error!);
            }
        }
        catch (EndOfStreamException ex)
        {
            return new CorruptChunkError("VorbisComment", "Block is truncated or a length field is incorrect.", ex);
        }

        return tags;
    }

    private Result ParseComment(string key, string value, SoundTags tags, ReadOptions options)
    {
        switch (key)
        {
            case "TITLE": tags.Title = value; break;
            case "ARTIST": tags.Artist = value; break;
            case "ALBUM": tags.Album = value; break;
            case "GENRE": tags.Genre = value; break;
            case "DATE":
            case "YEAR":
                if (uint.TryParse(value.Length >= 4 ? value[..4] : value, out var year)) tags.Year = year;
                break;
            case "TRACKNUMBER":
                if (uint.TryParse(value, out var track)) tags.TrackNumber = track;
                break;
            case "METADATA_BLOCK_PICTURE":
                if (options.ReadAlbumArt)
                {
                    var picResult = ParseMetadataBlockPicture(value);
                    if (picResult.IsFailure) return picResult;
                    tags.AlbumArt = picResult.Value;
                }
                break;
            case "LYRICS":
                tags.Lyrics ??= value;
                break;
        }
        return Result.Ok();
    }

    private Result<byte[]?> ParseMetadataBlockPicture(string base64Data)
    {
        try
        {
            var decoded = Convert.FromBase64String(base64Data);
            return ParsePictureBlock(new MemoryStream(decoded));
        }
        catch (FormatException ex)
        {
            return new CorruptChunkError("METADATA_BLOCK_PICTURE", "Base64 data is malformed.", ex);
        }
    }

    public Result<byte[]?> ParsePictureBlock(Stream stream)
    {
        try
        {
            using var reader = new BigEndianBinaryReader(stream);
            reader.ReadUInt32(); // Picture Type
            var mimeLen = reader.ReadUInt32();
            reader.ReadBytes((int)mimeLen);
            var descLen = reader.ReadUInt32();
            reader.ReadBytes((int)descLen);
            reader.ReadBytes(16); // geometry
            var dataLen = reader.ReadUInt32();
            return reader.ReadBytes((int)dataLen);
        }
        catch (Exception ex) when (ex is EndOfStreamException or ArgumentOutOfRangeException)
        {
            return new CorruptChunkError("PICTURE", "Picture block is truncated or contains invalid length fields.", ex);
        }
    }
}