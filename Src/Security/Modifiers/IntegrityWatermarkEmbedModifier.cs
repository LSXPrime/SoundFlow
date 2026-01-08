using SoundFlow.Abstracts;
using SoundFlow.Security.Configuration;
using SoundFlow.Security.Utils;

namespace SoundFlow.Security.Modifiers;

/// <summary>
/// Embeds a fragile integrity watermark using Block-Chained LSB Steganography.
/// This modifier calculates a Pearson hash of the current audio block and embeds it 
/// into the Least Significant Bits (LSB) of the subsequent block.
/// </summary>
public sealed class IntegrityWatermarkEmbedModifier : SoundModifier
{
    private readonly WatermarkConfiguration _config;
    
    private readonly float[] _currentBlock;
    private int _blockIndex;
    private byte _previousBlockHash;

    /// <inheritdoc />
    public override string Name { get; set; } = "Integrity Watermark Embedder";

    /// <summary>
    /// Initializes a new instance of the <see cref="IntegrityWatermarkEmbedModifier"/> class.
    /// </summary>
    /// <param name="config">Configuration settings containing the block size.</param>
    public IntegrityWatermarkEmbedModifier(WatermarkConfiguration config)
    {
        _config = config;
        _currentBlock = new float[_config.IntegrityBlockSize];
    }

    /// <inheritdoc />
    public override void Process(Span<float> buffer, int channels)
    {
        if (!Enabled) return;

        // Interleaved processing effectively treats multichannel audio as a single continuous stream. Any channel manipulation breaks the chain.
        for (var i = 0; i < buffer.Length; i++)
        {
            var sample = buffer[i];

            // 1. Embed the hash of the previous block into the first 8 samples of the current block, using the LSB of the mantissa.
            if (_blockIndex < 8)
            {
                var bit = (_previousBlockHash >> _blockIndex) & 1;
                sample = EmbedBit(sample, bit);
                buffer[i] = sample;
            }

            // 2. Accumulate the potentially modified sample into the current block buffer.
            _currentBlock[_blockIndex] = sample;
            _blockIndex++;

            // 3. Block Completed, Calculate hash for the next block.
            if (_blockIndex >= _config.IntegrityBlockSize)
            {
                _previousBlockHash = WatermarkingUtils.CalculatePearsonHash(_currentBlock);
                _blockIndex = 0;
            }
        }
    }

    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel) =>
        throw new NotSupportedException("Use the block-based Process method.");

    /// <summary>
    /// Embeds a single bit into the LSB of the float's mantissa representation.
    /// </summary>
    private static float EmbedBit(float sample, int bit)
    {
        var intRep = BitConverter.SingleToInt32Bits(sample);

        // Clear the Least Significant Bit
        intRep &= ~1;

        // OR in the data bit
        intRep |= (bit & 1);

        return BitConverter.Int32BitsToSingle(intRep);
    }
}