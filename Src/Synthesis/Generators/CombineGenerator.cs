using System.Buffers;
using SoundFlow.Interfaces;
using SoundFlow.Synthesis.Interfaces;
using SoundFlow.Synthesis.Voices;

namespace SoundFlow.Synthesis.Generators;

/// <summary>
/// An IGenerator that combines the output of two child generators using a specified function.
/// </summary>
internal sealed class CombineGenerator : IGenerator
{
    private readonly IGenerator _source1;
    private readonly IGenerator _source2;
    private readonly Func<float, float, float> _combineFunc;

    /// <summary>
    /// Initializes a new instance of the <see cref="CombineGenerator"/> class.
    /// </summary>
    /// <param name="source1">The first generator.</param>
    /// <param name="source2">The second generator.</param>
    /// <param name="combineFunc">A function to combine samples from source1 and source2.</param>
    public CombineGenerator(IGenerator source1, IGenerator source2, Func<float, float, float> combineFunc)
    {
        _source1 = source1;
        _source2 = source2;
        _combineFunc = combineFunc;
    }

    /// <inheritdoc />
    public int Generate(Span<float> buffer, VoiceContext context)
    {
        float[]? rentedBuffer = null;
        try
        {
            rentedBuffer = ArrayPool<float>.Shared.Rent(buffer.Length);
            var tempBuffer = rentedBuffer.AsSpan(0, buffer.Length);

            var len1 = _source1.Generate(buffer, context);
            var len2 = _source2.Generate(tempBuffer, context);
            var len = Math.Min(len1, len2);

            for (var i = 0; i < len; i++)
            {
                buffer[i] = _combineFunc(buffer[i], tempBuffer[i]);
            }
            return len;
        }
        finally
        {
            if (rentedBuffer != null)
            {
                ArrayPool<float>.Shared.Return(rentedBuffer);
            }
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        _source1.Reset();
        _source2.Reset();
    }
}