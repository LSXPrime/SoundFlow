using System.Text;
using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Utilities;
using SoundFlow.Metadata.Writers.Tags;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Writers.Format;

internal class AiffWriter : ISoundFormatWriter
{
    public async Task<Result> RemoveTagsAsync(string sourcePath, string destinationPath)
    {
        return await ProcessAiffFileAsync(sourcePath, destinationPath, null);
    }

    public async Task<Result> WriteTagsAsync(string sourcePath, string destinationPath, SoundTags tags)
    {
        return await ProcessAiffFileAsync(sourcePath, destinationPath, tags);
    }

    private async Task<Result> ProcessAiffFileAsync(string sourcePath, string destinationPath, SoundTags? tags)
    {
        try
        {
            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            await using var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            using var reader = new BigEndianBinaryReader(sourceStream);
            await using var writer = new BigEndianBinaryWriter(destStream);
            
            // 1. Read and validate "FORM" marker.
            if (sourceStream.Length < 12)
                return new CorruptChunkError("File", "File is too small to be a valid AIFF file.");
            var formMarker = reader.ReadString(4);
            if (formMarker != "FORM")
                return new HeaderNotFoundError("FORM");

            // 2. Read original size, we will recalculate this later.
            reader.ReadInt32(); 

            // 3. Read and write the FORM type ("AIFF" or "AIFC").
            var formType = reader.ReadString(4);
            writer.Write("FORM"u8.ToArray());
            writer.Write(0); // Placeholder for new size.
            writer.Write(Encoding.ASCII.GetBytes(formType));

            // The source stream is now correctly positioned at the start of the first inner chunk (12 bytes in).
            while (sourceStream.Position < sourceStream.Length)
            {
                if (sourceStream.Position + 8 > sourceStream.Length) break;
                
                var chunkId = reader.ReadString(4);
                var chunkSize = reader.ReadInt32();
                if (sourceStream.Position + chunkSize > sourceStream.Length)
                    return new CorruptChunkError(chunkId, "Chunk size exceeds file boundaries.");
                
                // Skip existing metadata chunks
                if (chunkId == "ID3 ")
                {
                    sourceStream.Seek(chunkSize, SeekOrigin.Current);
                }
                else // Copy other chunks
                {
                    writer.Write(Encoding.ASCII.GetBytes(chunkId));
                    writer.Write(chunkSize);
                    var chunkData = new byte[chunkSize];
                    await sourceStream.ReadExactlyAsync(chunkData);
                    writer.Write(chunkData);
                }
                // The padding byte must be skipped on EVERY chunk.
                if (chunkSize % 2 != 0)
                {
                    if (sourceStream.Position < sourceStream.Length)
                        sourceStream.ReadByte();
                }
            }

            // If writing new tags, build and append an ID3 chunk
            if (tags != null)
            {
                var id3Data = Id3V2Builder.Build(tags);
                writer.Write("ID3 "u8.ToArray());
                writer.Write(id3Data.Length);
                writer.Write(id3Data);
                if (id3Data.Length % 2 != 0) writer.Write((byte)0);
            }
            
            // Go back and write the correct FORM chunk size
            var finalSize = (int)destStream.Length - 8;
            writer.Seek(4, SeekOrigin.Begin);
            writer.Write(finalSize);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error("An unexpected error occurred while processing the AIFF file.", ex);
        }
    }
}