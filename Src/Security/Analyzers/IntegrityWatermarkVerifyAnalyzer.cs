using SoundFlow.Abstracts;
using SoundFlow.Security.Configuration;
using SoundFlow.Security.Utils;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Security.Analyzers;

/// <summary>
/// Verifies the integrity of an audio stream by validating block-chained watermarks.
/// Raises an event if the hash embedded in Block N does not match the computed hash of Block N-1.
/// </summary>
public sealed class IntegrityWatermarkVerifyAnalyzer : AudioAnalyzer
{
    private readonly WatermarkConfiguration _config;

    private readonly float[] _currentBlock;
    private int _blockIndex;

    private byte _calculatedHashOfPreviousBlock; // Hash(Block N-1)
    private byte _extractedHashFromCurrentBlock; // Extracted from LSB of Block N
    private bool _isFirstBlock = true;
    private long _totalBlocksProcessed;

    /// <inheritdoc />
    public override string Name { get; set; } = "Integrity Verifier";

    /// <summary>
    /// Occurs when an integrity violation is detected.
    /// The argument provided is the index of the block where verification failed.
    /// </summary>
    public event Action<long>? IntegrityViolationDetected;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntegrityWatermarkVerifyAnalyzer"/> class.
    /// </summary>
    public IntegrityWatermarkVerifyAnalyzer(AudioFormat format, WatermarkConfiguration config) : base(format)
    {
        _config = config;
        _currentBlock = new float[_config.IntegrityBlockSize];
    }

    /// <inheritdoc />
    protected override void Analyze(ReadOnlySpan<float> buffer, int channels)
    {
        foreach (var sample in buffer)
        {
            // 1. Extract embedded hash bits from the start of the block (first 8 samples)
            if (_blockIndex < 8)
            {
                var intRep = BitConverter.SingleToInt32Bits(sample);
                var bit = intRep & 1;
                if (bit == 1)
                {
                    _extractedHashFromCurrentBlock |= (byte)(1 << _blockIndex);
                }
            }

            // 2. Store sample for hashing (to verify the *next* block)
            _currentBlock[_blockIndex] = sample;
            _blockIndex++;

            // 3. Block Complete: Perform Verification
            if (_blockIndex >= _config.IntegrityBlockSize)
            {
                PerformBlockVerification();

                // Reset state for next block
                _blockIndex = 0;
                _extractedHashFromCurrentBlock = 0;
            }
        }
    }

    private void PerformBlockVerification()
    {
        // Skip verification for the first block, as we have no previous block to compare against.
        if (!_isFirstBlock)
        {
            // The watermark contract states: Block N contains the hash of Block N-1 (Previous vs Current block).
            if (_calculatedHashOfPreviousBlock != _extractedHashFromCurrentBlock)
            {
                IntegrityViolationDetected?.Invoke(_totalBlocksProcessed);
                Log.Warning(
                    $"Integrity violation at block {_totalBlocksProcessed}. Expected hash (from prev block): {_calculatedHashOfPreviousBlock}, Embedded hash: {_extractedHashFromCurrentBlock}");
            }
        }

        // Calculate the hash of the current block to carry forward to the next iteration.
        _calculatedHashOfPreviousBlock = WatermarkingUtils.CalculatePearsonHash(_currentBlock);

        _isFirstBlock = false;
        _totalBlocksProcessed++;
    }
}