using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Readers.Tags;
using SoundFlow.Metadata.Writers.Tags;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Writers.Format;

internal class Mp3Writer : ISoundFormatWriter
{
    public async Task<Result> RemoveTagsAsync(string sourcePath, string destinationPath)
    {
        try
        {
            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            var audioOffsetResult = await GetAudioDataOffsetAsync(sourceStream);
            if (audioOffsetResult.IsFailure) return audioOffsetResult;
            var audioDataOffset = audioOffsetResult.Value;
            
            await using var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            sourceStream.Position = audioDataOffset;
            await sourceStream.CopyToAsync(destStream);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error("An unexpected error occurred while removing MP3 tags.", ex);
        }
    }

    public async Task<Result> WriteTagsAsync(string sourcePath, string destinationPath, SoundTags tags)
    {
        try
        {
            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            var audioOffsetResult = await GetAudioDataOffsetAsync(sourceStream);
            if (audioOffsetResult.IsFailure) return audioOffsetResult;
            var audioDataOffset = audioOffsetResult.Value;
            
            var newTagData = Id3V2Builder.Build(tags);

            await using var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            await destStream.WriteAsync(newTagData);
            
            sourceStream.Position = audioDataOffset;
            await sourceStream.CopyToAsync(destStream);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error("An unexpected error occurred while writing MP3 tags.", ex);
        }
    }

    /// <summary>
    /// Finds the start of the audio data in an MP3 file by skipping over any ID3v2 tags.
    /// </summary>
    private async Task<Result<long>> GetAudioDataOffsetAsync(Stream stream)
    {
        stream.Position = 0;
        var id3Reader = new Id3V2Reader();
        var readResult = await id3Reader.ReadAsync(stream, new ReadOptions { ReadTags = true, ReadAlbumArt = false });
        if (readResult.IsFailure) return Result<long>.Fail(readResult.Error!);
        
        var (_, tagSize) = readResult.Value;
        return tagSize;
    }
}