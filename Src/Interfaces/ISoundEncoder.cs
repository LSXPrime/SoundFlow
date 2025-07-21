namespace SoundFlow.Interfaces;

/// <summary>
/// An interface representing a sound encoder.
/// </summary>
public interface ISoundEncoder : IDisposable
{
    /// <summary>
    /// Gets the value that determines if this encoder has been disposed.
    /// Once disposed, this instance is useless.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Encodes the given samples and writes them to the output.
    /// </summary>
    /// <param name="samples">The buffer containing the PCM samples to encode.</param>
    /// <returns>The number of samples successfully encoded.</returns>
    int Encode(Span<float> samples);
}
