using System.Text;
using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Readers.Format;
using SoundFlow.Metadata.Utilities;
using SoundFlow.Structs;

namespace SoundFlow.Metadata;

/// <summary>
///     Provides functionality to read audio file format and tag information.
/// </summary>
public static class SoundMetadataReader
{
    /// <summary>
    ///     Reads the audio information from the specified file path.
    /// </summary>
    /// <param name="filePath">The path to the audio file.</param>
    /// <param name="options">Configuration for what data to parse.</param>
    /// <returns>A Result object containing either the AudioFormatInfo or an error.</returns>
    public static Result<SoundFormatInfo> Read(string filePath, ReadOptions? options = null)
    {
        options ??= new ReadOptions();
        if (!File.Exists(filePath)) 
            return new NotFoundError("File", $"The file was not found at path: {filePath}");
        
        try
        {
            using var stream = File.OpenRead(filePath);
            return Read(stream, options);
        }
        catch (IOException ex)
        {
            return new Error("An I/O error occurred while opening the file.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new Error("Permission denied while opening the file.", ex);
        }
    }

    /// <summary>
    ///     Asynchronously reads the audio information from the specified file path.
    /// </summary>
    /// <param name="filePath">The path to the audio file.</param>
    /// <param name="options">Configuration for what data to parse.</param>
    /// <returns>A task representing the asynchronous operation, with a Result object containing either the AudioFormatInfo or an error.</returns>
    public static async Task<Result<SoundFormatInfo>> ReadAsync(string filePath, ReadOptions? options = null)
    {
        options ??= new ReadOptions();
        if (!File.Exists(filePath)) 
            return new NotFoundError("File", $"The file was not found at path: {filePath}");
        
        try
        {
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            return await GetReaderAndReadAsync(stream, options, true);
        }
        catch (IOException ex)
        {
            return new Error("An I/O error occurred while opening the file.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new Error("Permission denied while opening the file.", ex);
        }
    }

    /// <summary>
    ///     Reads the audio information from a stream. The stream must be readable and seekable.
    /// </summary>
    /// <param name="stream">The stream containing the audio data.</param>
    /// <param name="options">Configuration for what data to parse.</param>
    /// <returns>A Result object containing either the AudioFormatInfo or an error.</returns>
    public static Result<SoundFormatInfo> Read(Stream stream, ReadOptions? options = null)
    {
        options ??= new ReadOptions();
        return GetReaderAndReadAsync(stream, options, false, true).GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Asynchronously reads the audio information from a stream. The stream must be readable and seekable.
    /// </summary>
    /// <param name="stream">The stream containing the audio data.</param>
    /// <param name="options">Configuration for what data to parse.</param>
    /// <param name="leaveOpen">True to leave the stream open after reading, otherwise false.</param>
    /// <returns>A task representing the asynchronous operation, with a Result object containing either the AudioFormatInfo or an error.</returns>
    public static Task<Result<SoundFormatInfo>> ReadAsync(Stream stream, ReadOptions? options = null, bool leaveOpen = false)
    {
        options ??= new ReadOptions();
        return GetReaderAndReadAsync(stream, options, true, leaveOpen);
    }

    private static async Task<Result<SoundFormatInfo>> GetReaderAndReadAsync(Stream stream, ReadOptions options, bool async,
        bool leaveOpen = false)
    {
        if (stream is not { CanRead: true } || !stream.CanSeek)
            return new ValidationError("Stream must be readable and seekable.");

        var originalPosition = stream.Position;
        try
        {
            if (stream.Length < 12)
                return new CorruptChunkError("File", "File is too small to identify format.");
            
            var header = new byte[12];
            var bytesRead = async
                ? await stream.ReadAsync(header)
                : stream.Read(header, 0, header.Length);

            if (bytesRead < 4)
                return new CorruptChunkError("File", "File is too small to read a header.");

            // First, try to identify the format from the beginning of the file.
            var reader = GetReader(header);

            // If a format is not found, it might be because of an ID3 tag.
            // Check for an ID3 tag and try to identify the format after it.
            if (reader == null && Encoding.ASCII.GetString(header, 0, 3) == "ID3")
            {
                // Parse the synch-safe integer to get the tag size.
                var tagSize = (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];
                var audioDataOffset = 10 + tagSize;

                if (stream.Length > audioDataOffset)
                {
                    stream.Position = originalPosition + audioDataOffset;
                    var audioHeader = new byte[12];
                    bytesRead = async ? await stream.ReadAsync(audioHeader) : stream.Read(audioHeader, 0, audioHeader.Length);
                    if (bytesRead >= 4)
                    {
                        // Try to identify the format again from the audio data header.
                        reader = GetReader(audioHeader);
                    }
                }
            }

            // Finally, reset the stream position for the selected reader.
            stream.Position = originalPosition;

            if (reader == null)
                return new UnsupportedFormatError("Could not identify format from file header.");
            
            return async
                ? await reader.ReadAsync(stream, options)
                : reader.Read(stream, options);
        }
        finally
        {
            if (!leaveOpen)
            {
                if (async) await stream.DisposeAsync();
                else stream.Dispose();
            }
            else
            {
                stream.Position = originalPosition;
            }
        }
    }

    private static ISoundFormatReader? GetReader(byte[] header)
    {
        return FormatIdentifier.Identify(header) switch
        {
            AudioFormatType.Wav => new WavReader(),
            AudioFormatType.Aiff => new AiffReader(),
            AudioFormatType.Flac => new FlacReader(),
            AudioFormatType.Ogg => new OggReader(),
            AudioFormatType.M4a => new M4aReader(),
            AudioFormatType.Mp3 => new Mp3Reader(),
            AudioFormatType.Aac => new AacReader(),
            _ => null
        };
    }
}