using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Writers.Tags;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Writers.Format;

internal class FlacWriter : ISoundFormatWriter
{
    public async Task<Result> RemoveTagsAsync(string sourcePath, string destinationPath)
    {
        try
        {
            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            await using var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            using var reader = new BinaryReader(sourceStream);

            // Verify fLaC marker
            if (sourceStream.Length < 4 || new string(reader.ReadChars(4)) != "fLaC")
                return new HeaderNotFoundError("fLaC marker");
            destStream.Write("fLaC"u8);

            bool lastBlock;
            do
            {
                var headerByte = reader.ReadByte();
                lastBlock = (headerByte & 0x80) != 0;
                var blockType = (byte)(headerByte & 0x7F);
                
                var sizeBytes = reader.ReadBytes(3);
                var blockSize = (sizeBytes[0] << 16) | (sizeBytes[1] << 8) | sizeBytes[2];

                // Only copy the STREAMINFO block, skip all other metadata blocks.
                if (blockType == 0)
                {
                    // Write the original header and size.
                    destStream.WriteByte(headerByte);
                    destStream.Write(sizeBytes);
                    
                    var blockData = reader.ReadBytes(blockSize);
                    destStream.Write(blockData);
                }
                else
                {
                    // Skip other metadata blocks (VORBIS_COMMENT, PICTURE, etc.)
                    sourceStream.Seek(blockSize, SeekOrigin.Current);
                }

            } while (!lastBlock);
            
            // Copy the rest of the file (audio frames)
            await sourceStream.CopyToAsync(destStream);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error("An unexpected error occurred while removing FLAC tags.", ex);
        }
    }
    
    public async Task<Result> WriteTagsAsync(string sourcePath, string destinationPath, SoundTags tags)
    {
        try
        {
            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            await using var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            using var reader = new BinaryReader(sourceStream);

            // Write fLaC marker
            if (sourceStream.Length < 4 || new string(reader.ReadChars(4)) != "fLaC")
                return new HeaderNotFoundError("fLaC marker");
            destStream.Write("fLaC"u8);
            
            // Prepare new metadata blocks
            var vorbisBlock = VorbisCommentBuilder.Build(tags);
            var pictureBlock = VorbisCommentBuilder.BuildPictureBlock(tags);

            // Find the end of existing metadata to get audio data start position
            bool lastBlock;
            do
            {
                var headerByte = reader.ReadByte();
                lastBlock = (headerByte & 0x80) != 0;
                var blockType = (byte)(headerByte & 0x7F);
                
                var sizeBytes = reader.ReadBytes(3);
                var blockSize = (sizeBytes[0] << 16) | (sizeBytes[1] << 8) | sizeBytes[2];
                if (sourceStream.Position + blockSize > sourceStream.Length)
                    return new CorruptChunkError("Metadata Block", "Block size exceeds file boundaries.");
                
                // We must preserve the mandatory STREAMINFO block.
                if (blockType == 0)
                {
                    var isLast = pictureBlock == null && vorbisBlock.Length == 0;
                    var newHeader = (byte)(isLast ? 0x80 : 0x00); // Set last-block flag if needed
                    destStream.WriteByte(newHeader);
                    destStream.Write(sizeBytes);
                    destStream.Write(reader.ReadBytes(blockSize));
                }
                else
                {
                    // Skip all other old metadata blocks.
                    sourceStream.Seek(blockSize, SeekOrigin.Current);
                }
            } while (!lastBlock);

            var audioDataStartPos = sourceStream.Position;

            // Write the new VORBIS_COMMENT block
            var isVorbisLast = pictureBlock == null;
            WriteMetadataBlock(destStream, 4, vorbisBlock, isVorbisLast);

            // Write the new PICTURE block
            if (pictureBlock != null) WriteMetadataBlock(destStream, 6, pictureBlock, true);
            
            // Copy the audio data
            sourceStream.Position = audioDataStartPos;
            await sourceStream.CopyToAsync(destStream);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error("An unexpected error occurred while writing FLAC tags.", ex);
        }
    }

    private void WriteMetadataBlock(Stream stream, byte blockType, byte[] data, bool isLast)
    {
        var header = (byte)((isLast ? 0x80 : 0x00) | blockType);
        stream.WriteByte(header);
        
        // 24-bit size
        stream.WriteByte((byte)((data.Length >> 16) & 0xFF));
        stream.WriteByte((byte)((data.Length >> 8) & 0xFF));
        stream.WriteByte((byte)(data.Length & 0xFF));
        
        stream.Write(data);
    }
}