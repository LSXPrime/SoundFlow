namespace SoundFlow.Abstracts;

/// <summary>
/// An abstract representation of a sound modifier.
/// Implementations of this class alter audio data to apply various effects.
/// </summary>
public abstract class SoundModifier
{
    /// <summary>
    /// The name of the modifier.
    /// </summary>
    public virtual string Name { get; set; } = "Sound Modifier";

    /// <summary>
    /// Whether the modifier is enabled or not.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Applies the modifier to a buffer of audio samples.
    /// </summary>
    /// <param name="buffer">The buffer containing the audio samples to modify.</param>
    /// <param name="channels">The number of channels in the buffer.</param>
    public virtual void Process(Span<float> buffer, int channels)
    {
        if (!Enabled) return;
        
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = ProcessSample(buffer[i], i % channels);
        }
    }

    /// <summary>
    /// Processes a single audio sample.
    /// </summary>
    /// <param name="sample">The input audio sample.</param>
    /// <param name="channel">The channel the sample belongs to.</param>
    /// <returns>The modified audio sample.</returns>
    public abstract float ProcessSample(float sample, int channel);
}
