using SoundFlow.Synthesis.Voices;

namespace SoundFlow.Synthesis.Interfaces;

/// <summary>
/// Represents a fundamental component in a voice's signal generation chain.
/// An IGenerator produces an audio signal or a control signal.
/// </summary>
public interface IGenerator
{
    /// <summary>
    /// Generates a block of audio or control samples.
    /// </summary>
    /// <param name="buffer">The buffer to fill with generated samples. Generators should add their output to this buffer.</param>
    /// <param name="context">The context for the current voice, containing frequency, velocity, etc.</param>
    /// <returns>The number of samples written to the buffer.</returns>
    int Generate(Span<float> buffer, VoiceContext context);

    /// <summary>
    /// Resets the internal state of the generator.
    /// </summary>
    void Reset();
}