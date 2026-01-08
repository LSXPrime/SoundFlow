using System.Collections;
using SoundFlow.Abstracts;
using SoundFlow.Security.Configuration;
using SoundFlow.Security.Utils;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Security.Analyzers;

/// <summary>
/// Defines the internal state of the watermark extractor.
/// </summary>
internal enum ExtractorState
{
    SearchingForSync,
    ExtractingPayload,
    Complete
}

/// <summary>
/// Analyzes an audio stream to extract hidden ownership watermarks embedded using DSSS.
/// Implements a sliding window correlator to detect the synchronization sequence before decoding the payload.
/// </summary>
public sealed class OwnershipWatermarkExtractAnalyzer : AudioAnalyzer
{
    private readonly WatermarkConfiguration _config;
    
    // PRNG State
    private uint _rngState;
    
    private ExtractorState _state = ExtractorState.SearchingForSync;

    // Extraction Buffers
    private float _currentBitCorrelation;
    private int _samplesAccumulated;
    private readonly List<bool> _extractedBits = [];

    // Sync Detection
    private readonly bool[] _syncShiftRegister;
    private static readonly bool[] SyncSequence = 
        [true, false, true, false, true, false, true, false, true, true, false, false, true, true, false, false];

    /// <inheritdoc />
    public override string Name { get; set; } = "Ownership Watermark Extractor";

    /// <summary>
    /// Occurs when the payload extraction is complete.
    /// </summary>
    public event Action<BitArray>? PayloadExtracted;

    /// <summary>
    /// Initializes a new instance of the <see cref="OwnershipWatermarkExtractAnalyzer"/> class.
    /// </summary>
    public OwnershipWatermarkExtractAnalyzer(AudioFormat format, WatermarkConfiguration config) : base(format)
    {
        _config = config;
        
        // Initialize PRNG with the exact same seed method as the Embedder
        _rngState = WatermarkingUtils.GetStableHash(_config.Key);
        if (_rngState == 0) _rngState = 0xCAFEBABE;
        
        _syncShiftRegister = new bool[SyncSequence.Length];
    }

    /// <inheritdoc />
    protected override void Analyze(ReadOnlySpan<float> buffer, int channels)
    {
        if (_state == ExtractorState.Complete) return;

        for (var i = 0; i < buffer.Length; i += channels)
        {
            // Downmix to mono for analysis (simple average)
            float monoSample = 0;
            for (var c = 0; c < channels; c++) monoSample += buffer[i + c];
            monoSample /= channels;

            // Generate matched chip (must match embedder's sequence)
            var nextFloat = WatermarkingUtils.NextFloat(_rngState);
            _rngState = nextFloat.CurrentState;
            var chip = nextFloat.NextFloat > 0.5f ? 1.0f : -1.0f;

            // Audio below the embedding threshold (-50dB) is pure noise, ignore it.
            if (Math.Abs(monoSample) > 0.003f)
            {
                // Only accumulate if there is actual signal energy
                _currentBitCorrelation += monoSample * chip;
            }
            _samplesAccumulated++;

            // End of a bit period
            if (_samplesAccumulated >= _config.SpreadFactor)
            {
                var bit = _currentBitCorrelation > 0;
                ProcessExtractedBit(bit);

                // Reset for next bit
                _currentBitCorrelation = 0;
                _samplesAccumulated = 0;
            }
        }
    }

    private void ProcessExtractedBit(bool bit)
    {
        switch (_state)
        {
            case ExtractorState.SearchingForSync:
                // Shift bits into register
                Array.Copy(_syncShiftRegister, 1, _syncShiftRegister, 0, _syncShiftRegister.Length - 1);
                _syncShiftRegister[^1] = bit;

                // Check if register matches SyncSequence
                if (CheckSyncMatch())
                {
                    _state = ExtractorState.ExtractingPayload;
                    Log.Info("Watermark Sync Sequence Detected. Starting Payload Extraction.");
                }
                break;

            case ExtractorState.ExtractingPayload:
                _extractedBits.Add(bit);
                break;
        }
    }

    private bool CheckSyncMatch()
    {
        // Compare the shift register with the expected sync sequence.
        var errors = 0;
        const int maxErrors = 3; // Allow up to 3 bit errors tolerance in the 16-bit sync word (approx 20% BER tolerance on sync) for robustness against noise.

        for (var i = 0; i < SyncSequence.Length; i++)
        {
            if (_syncShiftRegister[i] != SyncSequence[i])
            {
                errors++;
            }
        }

        return errors <= maxErrors;
    }

    /// <summary>
    /// Finalizes extraction and attempts to parse the collected payload bits.
    /// This should be called when the audio stream ends.
    /// </summary>
    public void Finish()
    {
        if (_state != ExtractorState.ExtractingPayload || _extractedBits.Count <= 0) return;
        var payloadBits = new BitArray(_extractedBits.ToArray());
        PayloadExtracted?.Invoke(payloadBits);
        _state = ExtractorState.Complete;
    }
}