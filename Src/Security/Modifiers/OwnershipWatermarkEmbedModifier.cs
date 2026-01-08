using System.Collections;
using SoundFlow.Abstracts;
using SoundFlow.Security.Configuration;
using SoundFlow.Security.Payloads;
using SoundFlow.Security.Utils;

namespace SoundFlow.Security.Modifiers;

/// <summary>
/// Embeds a robust, invisible watermark into the audio stream using Direct Sequence Spread Spectrum (DSSS).
/// This modifier generates a pseudo-random noise sequence seeded by a secret key and modulates
/// the payload bits onto this noise. The noise is then added to the audio signal.
/// </summary>
public sealed class OwnershipWatermarkEmbedModifier : SoundModifier
{
    private readonly WatermarkConfiguration _config;
    private readonly BitArray _payloadBits;

    // PRNG State
    private uint _rngState;

    private int _currentBitIndex;
    private int _currentChipIndex;
    private bool _isComplete;

    // Sync sequence (16 bits): 1010101011001100
    private static readonly bool[] SyncSequence =
        [true, false, true, false, true, false, true, false, true, true, false, false, true, true, false, false];

    /// <inheritdoc />
    public override string Name { get; set; } = "Ownership Watermark Embedder";

    /// <summary>
    /// Initializes a new instance of the <see cref="OwnershipWatermarkEmbedModifier"/> class.
    /// </summary>
    /// <param name="payload">The data to embed.</param>
    /// <param name="config">Configuration settings.</param>
    public OwnershipWatermarkEmbedModifier(IWatermarkPayload payload, WatermarkConfiguration config)
    {
        _config = config;

        // Initialize PRNG with a stable hash of the key
        _rngState = WatermarkingUtils.GetStableHash(_config.Key);
        // Ensure non-zero seed
        if (_rngState == 0) _rngState = 0xCAFEBABE;

        // Construct payload
        var dataBits = payload.ToBits();
        _payloadBits = new BitArray(SyncSequence.Length + dataBits.Length);

        for (var i = 0; i < SyncSequence.Length; i++)
            _payloadBits[i] = SyncSequence[i];

        for (var i = 0; i < dataBits.Length; i++)
            _payloadBits[SyncSequence.Length + i] = dataBits[i];
    }

    /// <inheritdoc />
    public override void Process(Span<float> buffer, int channels)
    {
        if (_isComplete || !Enabled) return;

        // Process audio frame-by-frame (interleaved)
        for (var i = 0; i < buffer.Length; i += channels)
        {
            // 1. Generate deterministic chip (-1 or +1)
            var nextFloat = WatermarkingUtils.NextFloat(_rngState);
            _rngState = nextFloat.CurrentState;
            var chip = nextFloat.NextFloat > 0.5f ? 1.0f : -1.0f;
            
            // 2. Retrieve the current data bit (-1 for 0, +1 for 1).
            var bit = _payloadBits[_currentBitIndex] ? 1.0f : -1.0f;

            // 3. Apply to all channels
            for (var c = 0; c < channels; c++)
            {
                var sample = buffer[i + c];
                var magnitude = Math.Abs(sample);
                
                float adaptiveFactor;

                // If signal is below ~ -50dB (0.003), then protect fade-outs, silence, and reverb tails where watermark is obvious.
                if (magnitude < 0.003f)
                {
                    adaptiveFactor = 0.0f;
                }
                else
                {
                    // Square the magnitude. Audio 1.0 (Loud) -> Factor 1.0; Audio 0.5 (Med) -> Factor 0.25. This drastically reduces watermark power as volume decreases.
                    adaptiveFactor = magnitude * magnitude;

                    // Prevent it from blowing up on clipped audio (> 0dB)
                    if (adaptiveFactor > 1.0f) adaptiveFactor = 1.0f;
                }

                // Apply the squared curve to the config strength
                var effectiveStrength = _config.Strength * adaptiveFactor;

                if (effectiveStrength > 0) buffer[i + c] += effectiveStrength * chip * bit;
            }

            // 4. Advance State
            _currentChipIndex++;
            if (_currentChipIndex >= _config.SpreadFactor)
            {
                _currentChipIndex = 0;
                _currentBitIndex++;
                
                if (_currentBitIndex >= _payloadBits.Length)
                {
                    _isComplete = true;
                    break;
                }
            }
        }
    }

    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel) =>
        throw new NotSupportedException("Use block Process method.");
}