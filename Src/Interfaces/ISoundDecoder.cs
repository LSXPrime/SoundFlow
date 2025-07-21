using SoundFlow.Enums;

namespace SoundFlow.Interfaces;

/// <summary>
///     An interface representing a sound decoder.
/// </summary>
public interface ISoundDecoder : IDisposable
{
    /// <summary>
    ///     Gets the value that determines if this decoder has been disposed.
    ///     Once disposed, this instance is useless.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    ///     Gets the length of the PCM frames known to the decoder.
    ///     May be zero if audio is a stream or simply unknown to the audio format.
    /// </summary>
    int Length { get; }

    /// <summary>
    ///     Gets the format of the audio samples.
    /// </summary>
    SampleFormat SampleFormat { get; }
    
    /// <summary>
    /// Gets the number of channels in the decoded audio.
    /// </summary>
    int Channels { get; }

    /// <summary>
    /// Gets the sample rate of the decoded audio.
    /// </summary>
    int SampleRate { get; }

    /// <summary>
    ///     Seeks the decoder to a desired sample position from the beginning of the audio data.
    /// </summary>
    /// <param name="offset">Some offset in samples.</param>
    /// <returns>True, if seeking is possible and was successful.</returns>
    bool Seek(int offset);

    /// <summary>
    ///     Decodes the next block of samples, writing samples into <paramref name="samples" />.
    /// </summary>
    /// <param name="samples">The buffer to write decoded samples into.</param>
    /// <returns>The actual number of samples decoded, will be less than or equal to the length of <paramref name="samples" />.</returns>
    int Decode(Span<float> samples);

    /// <summary>
    ///     Raised when the end of the audio stream is reached.
    /// </summary>
    event EventHandler<EventArgs>? EndOfStreamReached;
}
