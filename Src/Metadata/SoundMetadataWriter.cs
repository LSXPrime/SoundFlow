using System.Text;
using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Utilities;
using SoundFlow.Metadata.Writers.Format;
using SoundFlow.Structs;

namespace SoundFlow.Metadata;

/// <summary>
/// Provides functionality to write or remove audio file metadata tags.
/// </summary>
public static class SoundMetadataWriter
{
    /// <summary>
    /// Writes the provided metadata tags to the specified audio file, overwriting any existing tags.
    /// This is a destructive operation that rewrites the entire file.
    /// </summary>
    /// <param name="filePath">The path to the audio file.</param>
    /// <param name="tags">The new tags to write to the file.</param>
    /// <returns>A Result object indicating success or failure.</returns>
    public static Result WriteTags(string filePath, SoundTags tags)
    {
        return WriteTagsAsync(filePath, tags).GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// Asynchronously writes the provided metadata tags to the specified audio file, overwriting any existing tags.
    /// This is a destructive operation that rewrites the entire file.
    /// </summary>
    /// <param name="filePath">The path to the audio file.</param>
    /// <param name="tags">The new tags to write to the file.</param>
    /// <returns>A task representing the asynchronous operation, with a Result object indicating success or failure.</returns>
    public static async Task<Result> WriteTagsAsync(string filePath, SoundTags? tags)
    {
        if (tags is null) return new ValidationError("Tags object cannot be null.");
        if (!File.Exists(filePath)) return new NotFoundError("File", $"The file was not found at path: {filePath}");

        var writerResult = GetWriter(filePath);
        if (writerResult.IsFailure || writerResult.Value is null) return writerResult;
        var writer = writerResult.Value;

        var tempFilePath = Path.GetTempFileName();
        try
        {
            var writeResult = await writer.WriteTagsAsync(filePath, tempFilePath, tags);
            if (writeResult.IsFailure) return writeResult;
            
            File.Move(tempFilePath, filePath, true); // Overwrite the original file with the new one
            return Result.Ok();
        }
        catch (Exception ex)
        {
            // Catch unexpected system-level exceptions during file operations.
            return new IOError("An unexpected error occurred during the file write operation.", ex);
        }
        finally
        {
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
        }
    }
    
    /// <summary>
    /// Removes all recognizable metadata tags from the specified audio file.
    /// This is a destructive operation that rewrites the entire file.
    /// </summary>
    /// <param name="filePath">The path to the audio file.</param>
    /// <returns>A Result object indicating success or failure.</returns>
    public static Result RemoveTags(string filePath)
    {
        return RemoveTagsAsync(filePath).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously removes all recognizable metadata tags from the specified audio file.
    /// This is a destructive operation that rewrites the entire file.
    /// </summary>
    /// <param name="filePath">The path to the audio file.</param>
    /// <returns>A task representing the asynchronous operation, with a Result object indicating success or failure.</returns>
    public static async Task<Result> RemoveTagsAsync(string filePath)
    {
        if (!File.Exists(filePath)) return new NotFoundError("File", $"The file was not found at path: {filePath}");
        
        var writerResult = GetWriter(filePath);
        if (writerResult.IsFailure || writerResult.Value is null) return writerResult;
        var writer = writerResult.Value;

        var tempFilePath = Path.GetTempFileName();
        try
        {
            var removeResult = await writer.RemoveTagsAsync(filePath, tempFilePath);
            if (removeResult.IsFailure) return removeResult;
            
            File.Move(tempFilePath, filePath, true);
            return Result.Ok();
        }
        catch(Exception ex)
        {
            return new IOError("An unexpected error occurred during the file write operation.", ex);
        }
        finally
        {
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
        }
    }

    private static Result<ISoundFormatWriter> GetWriter(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length < 12)
                return new CorruptChunkError("File", "File is too small to identify format for writing.");

            var buffer = new byte[12];
            stream.ReadExactly(buffer, 0, buffer.Length);

            // First, try to identify based on the file start
            var formatType = FormatIdentifier.Identify(buffer);

            // If not found, check if there's an ID3 tag we need to skip
            if (formatType == AudioFormatType.Unsupported && Encoding.ASCII.GetString(buffer, 0, 3) == "ID3")
            {
                var tagSize = (buffer[6] << 21) | (buffer[7] << 14) | (buffer[8] << 7) | buffer[9];
                var audioDataOffset = 10 + tagSize;

                if (stream.Length > audioDataOffset)
                {
                    stream.Position = audioDataOffset;
                    var audioHeader = new byte[12];
                    var bytesRead = stream.Read(audioHeader, 0, audioHeader.Length);
                    if (bytesRead >= 4)
                    {
                        formatType = FormatIdentifier.Identify(audioHeader);
                    }
                }
            }
            
            return formatType switch
            {
                AudioFormatType.Mp3 => Result<ISoundFormatWriter>.Ok(new Mp3Writer()),
                AudioFormatType.Flac => Result<ISoundFormatWriter>.Ok(new FlacWriter()),
                AudioFormatType.Ogg => Result<ISoundFormatWriter>.Ok(new OggWriter()),
                AudioFormatType.Wav => Result<ISoundFormatWriter>.Ok(new WavWriter()),
                AudioFormatType.Aiff => Result<ISoundFormatWriter>.Ok(new AiffWriter()),
                AudioFormatType.M4a => Result<ISoundFormatWriter>.Ok(new M4aWriter()),
                _ => new UnsupportedFormatError($"Writing tags for the format of '{Path.GetFileName(filePath)}' is not supported.")
            };
        }
        catch (IOException ex)
        {
            return new IOError("An I/O error occurred while identifying the file format.", ex);
        }
    }
}