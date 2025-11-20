using System.Text;
using SoundFlow.Metadata.Models;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Readers.Tags;

internal class Id3V2Reader
{
    public async Task<Result<(SoundTags?, long)>> ReadAsync(Stream stream, ReadOptions options)
    {
        var startPosition = stream.Position;
        var header = new byte[10];

        var bytesRead = await stream.ReadAsync(header.AsMemory(0, 10));
        if (bytesRead < 10 || Encoding.ASCII.GetString(header, 0, 3) != "ID3")
        {
            stream.Position = startPosition; // Reset position if no tag found
            return Result<(SoundTags?, long)>.Ok((null, 0));
        }

        // Synchsafe integer conversion
        var tagSize = (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];
        long tagEndPosition = 10 + tagSize;
        var tags = new SoundTags();

        try
        {
            while (stream.Position < tagEndPosition - 10)
            {
                var frameHeader = new byte[10];
                if (await stream.ReadAsync(frameHeader.AsMemory(0, 10)) < 10) break;

                var frameId = Encoding.ASCII.GetString(frameHeader, 0, 4);
                if (frameId.All(c => c == '\0')) break; // Padding

                var frameSize = (frameHeader[4] << 24) | (frameHeader[5] << 16) | (frameHeader[6] << 8) |
                                frameHeader[7];
                if (frameSize <= 0 || stream.Position + frameSize > tagEndPosition)
                    return new CorruptFrameError("ID3v2", "Invalid frame size or frame exceeds tag boundaries.");

                var nextFramePos = stream.Position + frameSize;

                var content = new byte[frameSize];
                await stream.ReadExactlyAsync(content, 0, frameSize);

                var parseResult = ParseFrame(frameId, content, tags, options);
                if (parseResult.IsFailure) return Result<(SoundTags?, long)>.Fail(parseResult.Error!);

                if (nextFramePos > stream.Length) break;
                stream.Position = nextFramePos;
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or ArgumentOutOfRangeException)
        {
            return new CorruptFrameError("ID3v2", "Tag is truncated or a frame is malformed.", ex);
        }

        return Result<(SoundTags?, long)>.Ok((tags, tagEndPosition));
    }

    private Result ParseFrame(string id, byte[] data, SoundTags tags, ReadOptions options)
    {
        if (data.Length <= 1) return Result.Ok();
        try
        {
            switch (id)
            {
                case "TIT2": tags.Title = GetString(data); break;
                case "TPE1": tags.Artist = GetString(data); break;
                case "TALB": tags.Album = GetString(data); break;
                case "TCON": tags.Genre = GetString(data).TrimStart('(').Split(')')[0]; break;
                case "TYER": // Year
                case "TDRC": // Recording Time
                    var yearString = GetString(data);
                    if (yearString.Length >= 4 && uint.TryParse(yearString.AsSpan(0, 4), out var year))
                        tags.Year = year;
                    break;
                case "TRCK":
                    if (uint.TryParse(GetString(data).Split('/')[0], out var track)) tags.TrackNumber = track;
                    break;
                case "APIC":
                    if (options.ReadAlbumArt) tags.AlbumArt = ParseApicFrame(data);
                    break;
                case "USLT":
                    tags.Lyrics ??= ParseUsltFrame(data);
                    break;
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new CorruptFrameError($"ID3v2 {id}", "Frame content could not be parsed.", ex);
        }
    }
    
    private string GetString(byte[] data)
    {
        if (data.Length == 0) return string.Empty;

        var encodingByte = data[0];
        var start = 1;
        int terminatorSize;
        Encoding encoding;

        switch (encodingByte)
        {
            case 0: // ISO-8859-1
                encoding = Encoding.GetEncoding("ISO-8859-1");
                terminatorSize = 1;
                break;
            case 1: // UTF-16 with BOM
                if (data.Length < 3) return string.Empty;
                // Check for BOM to determine endianness
                encoding = (data[1] == 0xFF && data[2] == 0xFE) ? Encoding.Unicode : Encoding.BigEndianUnicode;
                start = 3;
                terminatorSize = 2;
                break;
            case 2: // UTF-16BE without BOM
                encoding = Encoding.BigEndianUnicode;
                terminatorSize = 2;
                break;
            case 3: // UTF-8
                encoding = Encoding.UTF8;
                terminatorSize = 1; // Note: UTF-8 terminators are 1 byte, though chars can be multi-byte.
                break;
            default: // Fallback for unknown encodings
                return Encoding.Default.GetString(data, 1, data.Length - 1).TrimEnd('\0');
        }

        // Find the end of the string. It's either at the first null terminator
        // or at the end of the frame's data payload.
        var end = -1;
        for (var i = start; i <= data.Length - terminatorSize; i++)
        {
            bool isTerminator = (terminatorSize == 1)
                ? (data[i] == 0)
                : (data[i] == 0 && data[i + 1] == 0);

            if (isTerminator)
            {
                end = i;
                break;
            }
        }

        // If no terminator was found, the string occupies the rest of the available data.
        if (end == -1)
        {
            end = data.Length;
        }

        var length = end - start;
        if (length <= 0) return string.Empty;

        // **[REFINED FIX]**
        // For multi-byte encodings like UTF-16, a malformed tag can have a data length
        // that is not a multiple of the character size. To prevent a decoding error
        // (which produces '�'), we must truncate the length to the last valid character boundary.
        if (terminatorSize > 1 && length % terminatorSize != 0)
        {
            length -= length % terminatorSize; // e.g., for UTF-16, if length is 13, it becomes 12.
        }

        return length > 0 ? encoding.GetString(data, start, length) : string.Empty;
    }

    private static byte[]? ParseApicFrame(byte[] data)
    {
        var encodingByte = data[0];
        var pos = 1; // Position after encoding byte

        // MIME Type: ISO-8859-1 string, terminated with a single 0x00.
        var mimeEnd = Array.IndexOf(data, (byte)0, pos);
        if (mimeEnd == -1) return null;
        pos = mimeEnd + 1;

        // Picture Type: 1 byte.
        pos++;

        // Description: Encoded string, null-terminated.
        var terminatorSize = encodingByte is 1 or 2 ? 2 : 1;
        var descEnd = -1;

        // Search for the null terminator sequence.
        for (var i = pos; i <= data.Length - terminatorSize; i++)
            if (data[i] == 0 && (terminatorSize == 1 || data[i + 1] == 0))
            {
                descEnd = i;
                break;
            }

        if (descEnd == -1) return null; // Terminator not found

        pos = descEnd + terminatorSize;

        // Picture Data: The rest of the frame.
        return data.AsSpan(pos).ToArray();
    }

    private static string? ParseUsltFrame(byte[] data)
    {
        if (data.Length < 4) return null; // Must have at least Encoding (1) and Language (3)

        var encodingByte = data[0];
        const int pos = 4; // Position after encoding and language, start of descriptor.

        // Determine the string encoding and null terminator size
        Encoding encoding;
        var terminatorSize = 1;
        switch (encodingByte)
        {
            case 0:
                encoding = Encoding.GetEncoding("ISO-8859-1");
                break;
            case 1:
                encoding = Encoding.Unicode;
                terminatorSize = 2;
                break; // UTF-16
            case 2:
                encoding = Encoding.BigEndianUnicode;
                terminatorSize = 2;
                break; // UTF-16BE
            case 3:
                encoding = Encoding.UTF8;
                break;
            default:
                return null; // Unknown encoding
        }

        // Find the null terminator for the content descriptor to find where lyrics begin.
        var descEnd = -1;
        for (var i = pos; i <= data.Length - terminatorSize; i++)
            if (data[i] == 0 && (terminatorSize == 1 || data[i + 1] == 0))
            {
                descEnd = i;
                break;
            }

        // Some tools omit the descriptor and its terminator. If not found, assume it's empty.
        var lyricsStart = (descEnd != -1) ? descEnd + terminatorSize : pos;

        if (lyricsStart >= data.Length) return string.Empty; // No lyrics after descriptor

        switch (encodingByte)
        {
            // For UTF-16, a BOM may be present at the start of the lyrics text.
            case 1:
            {
                if (data.Length > lyricsStart + 1 && data[lyricsStart] == 0xFF && data[lyricsStart + 1] == 0xFE)
                {
                    encoding = Encoding.Unicode; // Little Endian
                    lyricsStart += 2;
                }
                else if (data.Length > lyricsStart + 1 && data[lyricsStart] == 0xFE && data[lyricsStart + 1] == 0xFF)
                {
                    encoding = Encoding.BigEndianUnicode; // Big Endian
                    lyricsStart += 2;
                }

                break;
            }
        }

        return lyricsStart >= data.Length
            ? string.Empty
            : encoding.GetString(data, lyricsStart, data.Length - lyricsStart)
                .TrimEnd('\0'); // Decode the final lyrics string from the remaining bytes.
    }
}