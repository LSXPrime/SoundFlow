using SoundFlow.Metadata.Models;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Abstracts;

/// <summary>
/// Internal interface for format-specific tag writers.
/// </summary>
internal interface ISoundFormatWriter
{
    /// <summary>
    /// Removes existing tags from a source file and writes the result to a destination file.
    /// </summary>
    Task<Result> RemoveTagsAsync(string sourcePath, string destinationPath);

    /// <summary>
    /// Writes the provided tags to a new destination file, using the audio data from the source file.
    /// </summary>
    Task<Result> WriteTagsAsync(string sourcePath, string destinationPath, SoundTags tags);
}