using SoundFlow.Metadata.Models;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Abstracts;

/// <summary>
///     Base class for format readers to reduce sync/async code duplication.
///     The synchronous Read method simply calls the asynchronous one and waits.
/// </summary>
internal abstract class BaseSoundFormatReader : ISoundFormatReader
{
    public Result<SoundFormatInfo> Read(Stream stream, ReadOptions options)
    {
        return ReadAsync(stream, options).GetAwaiter().GetResult();
    }

    public abstract Task<Result<SoundFormatInfo>> ReadAsync(Stream stream, ReadOptions options);
}