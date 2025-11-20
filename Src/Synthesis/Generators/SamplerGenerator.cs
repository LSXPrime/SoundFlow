using SoundFlow.Interfaces;
using SoundFlow.Synthesis.Interfaces;
using SoundFlow.Synthesis.Voices;

namespace SoundFlow.Synthesis.Generators;

/// <summary>
/// An IGenerator that plays back audio from a sample buffer, with pitch shifting.
/// </summary>
internal sealed class SamplerGenerator : IGenerator
{
    private readonly float[] _sampleData;
    private readonly int _rootNoteNumber;
    private readonly bool _isLooping;
    private double _readPosition;

    /// <summary>
    /// Initializes a new instance of the <see cref="SamplerGenerator"/> class.
    /// </summary>
    /// <param name="sampleData">The raw audio data of the sample.</param>
    /// <param name="rootNoteNumber">The MIDI note number at which the sample plays at its original pitch.</param>
    /// <param name="isLooping">Whether the sample should loop.</param>
    public SamplerGenerator(float[] sampleData, int rootNoteNumber, bool isLooping)
    {
        _sampleData = sampleData;
        _rootNoteNumber = rootNoteNumber;
        _isLooping = isLooping;
        _readPosition = 0;
    }

    /// <inheritdoc />
    public int Generate(Span<float> buffer, VoiceContext context)
    {
        var rootFrequency = 440.0 * Math.Pow(2.0, (_rootNoteNumber - 69.0) / 12.0);
        var playbackRate = context.Frequency / rootFrequency;

        for (var i = 0; i < buffer.Length; i++)
        {
            var index1 = (int)_readPosition;
            var index2 = index1 + 1;
            var fraction = (float)(_readPosition - index1);

            if (_isLooping)
            {
                index1 %= _sampleData.Length;
                index2 %= _sampleData.Length;
            }

            if (index2 >= _sampleData.Length)
            {
                buffer[i] = 0; // End of sample if not looping
                continue;
            }

            var sample1 = _sampleData[index1];
            var sample2 = _sampleData[index2];
            buffer[i] = sample1 + fraction * (sample2 - sample1); // Linear interpolation

            _readPosition += playbackRate;
        }

        return buffer.Length;
    }

    /// <inheritdoc />
    public void Reset()
    {
        _readPosition = 0;
    }
}