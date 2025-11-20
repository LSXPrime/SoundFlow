using SoundFlow.Metadata.Models;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Abstracts;

/// <summary>
///     Internal interface for format-specific parsers.
/// </summary>
internal interface ISoundFormatReader
{
    Result<SoundFormatInfo> Read(Stream stream, ReadOptions options);
    Task<Result<SoundFormatInfo>> ReadAsync(Stream stream, ReadOptions options);
}