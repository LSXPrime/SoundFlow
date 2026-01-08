namespace SoundFlow.Components;

/// <summary>
/// Defines performance presets for the WSOLA time stretcher.
/// These presets balance between CPU usage, latency, and audio quality.
/// </summary>
public enum WsolaPerformancePreset
{
    /// <summary>
    /// Optimized for low latency and low CPU usage. 
    /// Suitable for speech or when performance is critical. 
    /// Window: 1024, Hop: 512, Search: 128.
    /// </summary>
    Fast,

    /// <summary>
    /// The standard configuration offering a good trade-off between quality and performance.
    /// Suitable for general music playback.
    /// Window: 2048, Hop: 1024, Search: 256.
    /// </summary>
    Balanced,

    /// <summary>
    /// Optimized for smoother audio stretching with fewer artifacts, at the cost of higher latency and CPU usage.
    /// Window: 4096, Hop: 2048, Search: 512.
    /// </summary>
    HighQuality,

    /// <summary>
    /// Maximum quality configuration for complex polyphonic material.
    /// Window: 8192, Hop: 4096, Search: 1024.
    /// </summary>
    Audiophile
}

/// <summary>
/// Configuration container for WSOLA parameters.
/// </summary>
public class WsolaConfig
{
    /// <summary>
    /// The length of the analysis window in frames.
    /// Should be even (typically power of 2).
    /// </summary>
    public int WindowSizeFrames { get; }

    /// <summary>
    /// The hop size in frames used by the synthesis stage (output hop).
    /// This value remains fixed across speeds. The analysis hop is derived from this value and <see cref="WsolaTimeStretcher.SetSpeed"/>.
    /// Typically, 1/2 of <see cref="WindowSizeFrames"/> for stable overlap behavior.
    /// </summary>
    public int AnalysisHopFrames { get; }

    /// <summary>
    /// The range of frames to search for the best overlap match.
    /// </summary>
    public int SearchRadiusFrames { get; }

    /// <summary>
    /// Creates a custom WSOLA configuration.
    /// </summary>
    /// <param name="windowSizeFrames">Length of the analysis window in frames. Should be even (typically power of 2).</param>
    /// <param name="analysisHopFrames">Synthesis hop size in frames (output hop). Must be positive and less than window size.</param>
    /// <param name="searchRadiusFrames">The range of frames to search for the best overlap match.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if values are invalid.</exception>
    public WsolaConfig(int windowSizeFrames, int analysisHopFrames, int searchRadiusFrames)
    {
        if (windowSizeFrames < 128)
            throw new ArgumentOutOfRangeException(nameof(windowSizeFrames), "Window size must be at least 128 frames.");
        if (windowSizeFrames % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(windowSizeFrames), "Window size must be even.");
        if (analysisHopFrames <= 0 || analysisHopFrames >= windowSizeFrames)
            throw new ArgumentOutOfRangeException(nameof(analysisHopFrames), "Hop size must be positive and less than window size.");
        if (searchRadiusFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(searchRadiusFrames), "Search radius cannot be negative.");

        WindowSizeFrames = windowSizeFrames;
        AnalysisHopFrames = analysisHopFrames;
        SearchRadiusFrames = searchRadiusFrames;
    }

    /// <summary>
    /// Creates a configuration based on a predefined preset.
    /// </summary>
    /// <param name="preset">The performance preset.</param>
    /// <returns>A configured WsolaConfig instance.</returns>
    public static WsolaConfig FromPreset(WsolaPerformancePreset preset)
    {
        return preset switch
        {
            WsolaPerformancePreset.Fast => new WsolaConfig(1024, 512, 128),
            WsolaPerformancePreset.Balanced => new WsolaConfig(2048, 1024, 256),
            WsolaPerformancePreset.HighQuality => new WsolaConfig(4096, 2048, 512),
            WsolaPerformancePreset.Audiophile => new WsolaConfig(8192, 4096, 1024),
            _ => new WsolaConfig(1024, 512, 128) // Default to Fast
        };
    }
}

/// <summary>
/// Implements the WSOLA (Waveform Similarity Overlap-Add) algorithm for real-time time stretching
/// and pitch preservation of audio. It allows changing playback speed without altering pitch.
/// Optimized using unsafe pointer arithmetic and SIMD (Single Instruction, Multiple Data).
/// </summary>
public class WsolaTimeStretcher
{
    private int _channels;
    private float _speed = 1.0f;

    // Configurable Parameters
    private int _windowSizeFrames;
    private int _synthesisHopFrames;
    private int _searchRadiusFrames;

    // Speed-Dependent Parameters
    private int _analysisHopFrames;

    private int _windowSizeSamples;
    private float[] _inputBufferInternal = [];
    private int _inputBufferValidSamples;
    private int _inputBufferReadPos;
    private int _nominalInputPos;

    private float[] _prevOutputTail = [];
    private int _actualPrevTailLength;
    private float[] _currentAnalysisFrame = [];
    private float[] _outputOverlapBuffer = [];
    private bool _isFirstFrame = true;
    private bool _isFlushing;

    /// <summary>
    /// Initializes a new instance of the <see cref="WsolaTimeStretcher"/> class.
    /// </summary>
    /// <param name="initialChannels">The initial number of audio channels. Defaults to 2 if not positive.</param>
    /// <param name="initialSpeed">The initial playback speed. Defaults to 1.0f.</param>
    /// <param name="config">Optional configuration object. Defaults to Fast preset.</param>
    public WsolaTimeStretcher(int initialChannels = 2, float initialSpeed = 1.0f, WsolaConfig? config = null)
    {
        ApplyConfig(config ?? WsolaConfig.FromPreset(WsolaPerformancePreset.Fast));

        initialChannels = initialChannels <= 0 ? 2 : initialChannels;
        SetChannels(initialChannels);
        SetSpeed(initialSpeed);
    }

    /// <summary>
    /// Updates the internal configuration parameters.
    /// This is a heavy operation that will clear buffers and reset the processing state.
    /// </summary>
    /// <param name="config">The new configuration to apply.</param>
    public void Configure(WsolaConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // If config hasn't effectively changed, do nothing
        if (_windowSizeFrames == config.WindowSizeFrames &&
            _synthesisHopFrames == config.AnalysisHopFrames &&
            _searchRadiusFrames == config.SearchRadiusFrames)
        {
            return;
        }

        ApplyConfig(config);

        // Re-initialize buffers with new sizes (Force re-allocation)
        var currentChannels = _channels;
        _channels = -1;
        SetChannels(currentChannels);

        // Recalculate speed-dependent analysis hop and ensure buffer sizing
        SetSpeed(_speed);
    }

    /// <summary>
    /// Updates the internal configuration parameters based on a preset.
    /// </summary>
    /// <param name="preset">The performance preset to apply.</param>
    public void Configure(WsolaPerformancePreset preset)
    {
        Configure(WsolaConfig.FromPreset(preset));
    }

    private void ApplyConfig(WsolaConfig config)
    {
        _windowSizeFrames = config.WindowSizeFrames;
        _synthesisHopFrames = config.AnalysisHopFrames;
        _searchRadiusFrames = config.SearchRadiusFrames;

        // Ensure derived hop is valid even before SetSpeed runs.
        _analysisHopFrames = Math.Max(1, _synthesisHopFrames);
    }

    /// <summary>
    /// Sets the number of audio channels for the time stretcher. Reinitializes internal buffers if channels change.
    /// </summary>
    /// <param name="channels">The number of audio channels.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if channels is not positive.</exception>
    public void SetChannels(int channels)
    {
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels), "Channels must be positive.");
        if (_channels == channels) return;

        _channels = channels;
        _windowSizeSamples = _windowSizeFrames * _channels;

        // Buffer A for correlation / tail storage
        _prevOutputTail = new float[Math.Max(_channels, _windowSizeSamples - _channels)];

        // _currentAnalysisFrame holds the raw data for the current window
        _currentAnalysisFrame = new float[_windowSizeSamples];

        // Overlap buffer holds the full reconstructed window
        _outputOverlapBuffer = new float[_windowSizeSamples];

        EnsureInternalInputBufferCapacity();
        ResetState();
    }

    /// <summary>
    /// Sets the playback speed for time stretching.
    /// </summary>
    /// <param name="speed">The desired playback speed (e.g., 0.5 for half speed, 2.0 for double speed).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if speed is not positive.</exception>
    public void SetSpeed(float speed)
    {
        if (speed <= 0) throw new ArgumentOutOfRangeException(nameof(speed), "Speed must be positive.");
        _speed = speed;

        // Keep synthesis hop fixed. Derive analysis hop from speed.
        _analysisHopFrames = (int)Math.Max(1, Math.Round(_synthesisHopFrames * _speed));

        EnsureInternalInputBufferCapacity();
    }

    /// <summary>
    /// Ensures the internal input buffer is large enough for the current configuration and speed.
    /// This accounts for analysis hop, search radius, and one full window of lookahead.
    /// </summary>
    private void EnsureInternalInputBufferCapacity()
    {
        if (_channels <= 0) return;

        var maxInputReachFrames = _analysisHopFrames + _searchRadiusFrames + _windowSizeFrames;
        var requiredSamples = maxInputReachFrames * _channels * 3; // *3 for safety/overlap margin

        if (_inputBufferInternal.Length < requiredSamples)
        {
            _inputBufferInternal = new float[requiredSamples];
        }
    }

    /// <summary>
    /// Gets the minimum number of input samples required in the internal buffer to perform a processing step.
    /// </summary>
    public int MinInputSamplesToProcess =>
        (_analysisHopFrames + _searchRadiusFrames) * _channels + _windowSizeSamples;

    /// <summary>
    /// Resets the internal state of the time stretcher, clearing all buffers and flags.
    /// This should be called when seeking or stopping playback.
    /// </summary>
    private void ResetState()
    {
        _inputBufferValidSamples = 0;
        _inputBufferReadPos = 0;
        _nominalInputPos = 0;
        Array.Clear(_prevOutputTail, 0, _prevOutputTail.Length);
        _actualPrevTailLength = 0;
        _isFirstFrame = true;
        _isFlushing = false;
    }

    /// <summary>
    /// Resets the internal state of the time stretcher, clearing all buffers and flags.
    /// </summary>
    public void Reset() => ResetState();

    /// <summary>
    /// Gets the current target playback speed set for the time stretcher.
    /// </summary>
    /// <returns>The current playback speed.</returns>
    public float GetTargetSpeed() => _speed;

    /// <summary>
    /// Calculates a raised-cosine fade value for crossfading.
    /// </summary>
    /// <param name="i">The current frame index.</param>
    /// <param name="n">The total number of frames in the fade.</param>
    /// <returns>A value between 0.0 and 1.0 (approximating an S-curve).</returns>
    private static float Fade(int i, int n)
    {
        if (n <= 1) return 1f;
        return 0.5f - 0.5f * MathF.Cos(MathF.PI * i / (n - 1));
    }

    /// <summary>
    /// Processes a segment of audio data for time stretching.
    /// </summary>
    /// <param name="input">The input audio data to be processed.</param>
    /// <param name="output">The span to write the processed audio data to.</param>
    /// <param name="samplesConsumedFromInputBuffer">Output parameter: The number of samples consumed from the input span.</param>
    /// <param name="sourceSamplesRepresentedByOutput">Output parameter: The number of *original* source samples that the generated output represents.</param>
    /// <returns>The number of samples written to the output span.</returns>
    public unsafe int Process(ReadOnlySpan<float> input, Span<float> output,
        out int samplesConsumedFromInputBuffer,
        out int sourceSamplesRepresentedByOutput)
    {
        samplesConsumedFromInputBuffer = 0;
        sourceSamplesRepresentedByOutput = 0;
        if (_channels == 0 || output.IsEmpty) return 0;

        // 1. Manage Input Buffer
        if (!input.IsEmpty)
        {
            // If the buffer is full, or we have significant discarded data, shift left.
            if (_inputBufferReadPos > 0 && _inputBufferValidSamples > _inputBufferReadPos)
            {
                Buffer.BlockCopy(_inputBufferInternal, _inputBufferReadPos * sizeof(float), _inputBufferInternal, 0,
                    (_inputBufferValidSamples - _inputBufferReadPos) * sizeof(float));

                _nominalInputPos -= _inputBufferReadPos;
                _inputBufferValidSamples -= _inputBufferReadPos;
                _inputBufferReadPos = 0;
            }

            var spaceInInputBuffer = _inputBufferInternal.Length - _inputBufferValidSamples;
            var toCopy = Math.Min(spaceInInputBuffer, input.Length);
            if (toCopy > 0)
            {
                input.Slice(0, toCopy).CopyTo(_inputBufferInternal.AsSpan(_inputBufferValidSamples));
                _inputBufferValidSamples += toCopy;
                samplesConsumedFromInputBuffer = toCopy;
            }
        }

        var samplesWrittenToOutput = 0;
        var totalSourceSamplesForThisCall = 0;

        var currentWindowSizeFrames = _windowSizeFrames;
        var currentSearchRadiusFrames = _searchRadiusFrames;
        var currentChannels = _channels;
        var currentWindowSizeSamples = _windowSizeSamples;

        var analysisHopSamples = _analysisHopFrames * currentChannels;
        var searchRadiusSamples = currentSearchRadiusFrames * currentChannels;

        // Fixed synthesis parameters
        var hopSynFrames = _synthesisHopFrames;
        var overlapFrames = currentWindowSizeFrames - hopSynFrames;
        if (overlapFrames < 0) overlapFrames = 0;

        var hopSynSamples = hopSynFrames * currentChannels;
        var overlapSamples = overlapFrames * currentChannels;

        // Pin arrays once to avoid pinning/unpinning in the tight loop
        fixed (float* pInputBase = _inputBufferInternal)
        fixed (float* pPrevTailBase = _prevOutputTail)
        fixed (float* pOutputOverlap = _outputOverlapBuffer)
        fixed (float* pCurrentAnalysis = _currentAnalysisFrame)
        {
            while (samplesWrittenToOutput < output.Length)
            {
                // Determine the base (nominal) position in the input buffer for this frame's processing.
                var basePosInInput = _nominalInputPos;

                // Calculate required valid samples in the buffer for the search and a full window read.
                var requiredSamples = basePosInInput + searchRadiusSamples + currentWindowSizeSamples;

                // Check Data Availability
                if (_inputBufferValidSamples < requiredSamples)
                {
                    if (!_isFlushing || (_inputBufferValidSamples < basePosInInput + currentWindowSizeSamples))
                    {
                        // Shift if we have dead space and need to read more
                        if (_inputBufferReadPos > 0)
                        {
                            Buffer.BlockCopy(_inputBufferInternal, _inputBufferReadPos * sizeof(float),
                                _inputBufferInternal, 0,
                                (_inputBufferValidSamples - _inputBufferReadPos) * sizeof(float));

                            _nominalInputPos -= _inputBufferReadPos;
                            _inputBufferValidSamples -= _inputBufferReadPos;
                            _inputBufferReadPos = 0;
                        }

                        sourceSamplesRepresentedByOutput = totalSourceSamplesForThisCall;
                        return samplesWrittenToOutput;
                    }
                }

                var bestOffsetSamples = 0;

                // WSOLA Search Phase
                if (!_isFirstFrame && _actualPrevTailLength > 0 && overlapFrames > 0)
                {
                    // Compare only within the overlap region to reduce artifacts.
                    var availablePrevTailFrames = _actualPrevTailLength / currentChannels;
                    var compareFrames = Math.Min(overlapFrames, availablePrevTailFrames);
                    var compareSamples = compareFrames * currentChannels;

                    const int minValidOverlapDivider = 4;
                    var minValidOverlapForSearch = currentSearchRadiusFrames / minValidOverlapDivider;

                    // Fast energy check using Left channel only to avoid stereo cancellation issues.
                    float prevTailEnergy = 0;
                    if (compareFrames > 0)
                    {
                        for (var iF = 0; iF < compareFrames; ++iF)
                        {
                            var idx = iF * currentChannels; // Left channel at idx
                            var v = pPrevTailBase[idx];
                            prevTailEnergy += v * v;
                        }
                    }

                    var silenceThreshold = 1e-7f * compareFrames;

                    if (prevTailEnergy > silenceThreshold &&
                        compareFrames > minValidOverlapForSearch &&
                        compareFrames > 0)
                    {
                        var maxNcc = -2.0;

                        // Pre-Calculate Buffer A (Previous Output Tail) Stats - Left channel only
                        double sumA = 0;
                        for (var i = 0; i < compareFrames; ++i)
                        {
                            sumA += pPrevTailBase[i * currentChannels];
                        }
                        var meanA = (float)(sumA / compareFrames);

                        double sumADevSq = 0;
                        for (var i = 0; i < compareFrames; ++i)
                        {
                            var val = pPrevTailBase[i * currentChannels];
                            var d = val - meanA;
                            sumADevSq += d * d;
                        }

                        // Search Loop
                        for (var currentDeltaFrames = -currentSearchRadiusFrames;
                             currentDeltaFrames <= currentSearchRadiusFrames;
                             currentDeltaFrames++)
                        {
                            var currentDeltaSamples = currentDeltaFrames * currentChannels;
                            var candidateSegmentStartSample = basePosInInput + currentDeltaSamples;

                            // Bounds Check
                            if (candidateSegmentStartSample < 0 ||
                                candidateSegmentStartSample + compareSamples > _inputBufferValidSamples)
                            {
                                continue;
                            }

                            var pB = pInputBase + candidateSegmentStartSample;

                            // Calculate Mean B - Left channel only
                            double sumB = 0;
                            for (var i = 0; i < compareFrames; ++i)
                            {
                                sumB += pB[i * currentChannels];
                            }
                            var meanB = (float)(sumB / compareFrames);

                            // Calculate Cross-Correlation and SumBDevSq
                            double sumBDevSq = 0;
                            double dotProductDev = 0;

                            for (var i = 0; i < compareFrames; ++i)
                            {
                                var a = pPrevTailBase[i * currentChannels];
                                var b = pB[i * currentChannels];

                                var dA = a - meanA;
                                var dB = b - meanB;

                                dotProductDev += dA * dB;
                                sumBDevSq += dB * dB;
                            }

                            // NCC Calculation
                            double currentNcc;
                            var denominator = Math.Sqrt(sumADevSq * sumBDevSq);
                            if (denominator < 1e-9)
                                currentNcc = (sumADevSq < 1e-9 && sumBDevSq < 1e-9) ? 1.0 : 0.0;
                            else
                                currentNcc = dotProductDev / denominator;

                            const float nccQualityThreshold = 0.02f;

                            // Early exit if we find a near-perfect match.
                            if (currentNcc > 0.995)
                            {
                                maxNcc = currentNcc;
                                bestOffsetSamples = currentDeltaSamples;
                                break;
                            }

                            if (currentNcc > maxNcc + nccQualityThreshold)
                            {
                                maxNcc = currentNcc;
                                bestOffsetSamples = currentDeltaSamples;
                            }
                            else if (currentNcc > maxNcc - nccQualityThreshold)
                            {
                                if (Math.Abs(currentDeltaSamples) < Math.Abs(bestOffsetSamples))
                                {
                                    maxNcc = currentNcc;
                                    bestOffsetSamples = currentDeltaSamples;
                                }
                            }
                        }
                    }
                }

                // Analysis & Synthesis Phase
                var chosenSegmentStartSampleInInput = basePosInInput + bestOffsetSamples;

                // If flush handling forces us to read past valid data, clamp
                if (chosenSegmentStartSampleInInput + currentWindowSizeSamples > _inputBufferValidSamples)
                {
                    if (_isFlushing)
                    {
                        chosenSegmentStartSampleInInput = _inputBufferValidSamples - currentWindowSizeSamples;
                        if (chosenSegmentStartSampleInInput < 0) chosenSegmentStartSampleInInput = 0;
                    }
                    else
                    {
                        break; // Should have been caught by Availability Check, but safe fallback
                    }
                }

                // Extract Raw Frame
                Buffer.MemoryCopy(
                    pInputBase + chosenSegmentStartSampleInInput,
                    pCurrentAnalysis,
                    currentWindowSizeSamples * sizeof(float),
                    currentWindowSizeSamples * sizeof(float));

                // Overlap-Add with Crossfade
                if (!_isFirstFrame && _actualPrevTailLength > 0 && overlapFrames > 0)
                {
                    var framesToFade = Math.Min(overlapFrames, _actualPrevTailLength / currentChannels);

                    for (var f = 0; f < framesToFade; f++)
                    {
                        var w = Fade(f, framesToFade);
                        var inv = 1f - w;
                        var baseIdx = f * currentChannels;

                        for (var ch = 0; ch < currentChannels; ch++)
                        {
                            pOutputOverlap[baseIdx + ch] =
                                pPrevTailBase[baseIdx + ch] * inv +
                                pCurrentAnalysis[baseIdx + ch] * w;
                        }
                    }

                    // Remainder of the window (after overlap) is just the current frame
                    var startSamples = framesToFade * currentChannels;
                    if (startSamples < currentWindowSizeSamples)
                    {
                        Buffer.MemoryCopy(
                            pCurrentAnalysis + startSamples,
                            pOutputOverlap + startSamples,
                            (currentWindowSizeSamples - startSamples) * sizeof(float),
                            (currentWindowSizeSamples - startSamples) * sizeof(float));
                    }
                }
                else
                {
                    // First frame: just copy current frame
                    Buffer.MemoryCopy(
                        pCurrentAnalysis,
                        pOutputOverlap,
                        currentWindowSizeSamples * sizeof(float),
                        currentWindowSizeSamples * sizeof(float));
                }

                // Output
                var availableInOutputSpan = output.Length - samplesWrittenToOutput;
                var actualCopyToOutput = Math.Min(hopSynSamples, availableInOutputSpan);

                if (actualCopyToOutput > 0)
                {
                    new Span<float>(pOutputOverlap, actualCopyToOutput).CopyTo(output.Slice(samplesWrittenToOutput));
                    samplesWrittenToOutput += actualCopyToOutput;

                    // Track source samples represented by this output segment using fixed ratio speed = analysisHop / synthesisHop.
                    totalSourceSamplesForThisCall += _synthesisHopFrames > 0
                        ? (int)Math.Round((double)actualCopyToOutput / (_synthesisHopFrames * currentChannels) * analysisHopSamples)
                        : analysisHopSamples;
                }

                // Save Tail
                if (overlapSamples > 0)
                {
                    if (_prevOutputTail.Length >= overlapSamples)
                    {
                        Buffer.MemoryCopy(
                            pOutputOverlap + hopSynSamples,
                            pPrevTailBase,
                            _prevOutputTail.Length * sizeof(float),
                            overlapSamples * sizeof(float));
                    }
                    else
                    {
                        new Span<float>(pPrevTailBase, _prevOutputTail.Length).Clear();
                    }
                }

                _actualPrevTailLength = overlapSamples;
                _isFirstFrame = false;

                // Advance the nominal position by the speed-dependent analysis hop.
                _nominalInputPos += analysisHopSamples;

                // Update discard pointer (keep enough history for negative search offsets).
                var safeDiscardPoint = _nominalInputPos - searchRadiusSamples;
                if (safeDiscardPoint > _inputBufferReadPos)
                {
                    _inputBufferReadPos = safeDiscardPoint;
                }
            }
        }

        var remainingInternalInput = _inputBufferValidSamples - _inputBufferReadPos;
        if (_inputBufferReadPos > 0 && remainingInternalInput > 0)
        {
            Buffer.BlockCopy(_inputBufferInternal, _inputBufferReadPos * sizeof(float), _inputBufferInternal, 0,
                remainingInternalInput * sizeof(float));

            _nominalInputPos -= _inputBufferReadPos;
            _inputBufferValidSamples = remainingInternalInput;
            _inputBufferReadPos = 0;
        }
        else if (remainingInternalInput <= 0)
        {
            _inputBufferValidSamples = 0;
            _inputBufferReadPos = 0;
            _nominalInputPos = 0;
        }

        sourceSamplesRepresentedByOutput = totalSourceSamplesForThisCall;
        return samplesWrittenToOutput;
    }

    /// <summary>
    /// Flushes any remaining buffered audio data through the time stretcher.
    /// This is typically called at the end of a stream to ensure all data is processed.
    /// </summary>
    /// <param name="output">The span to write the flushed audio data to.</param>
    /// <returns>The total number of samples written to the output span during flushing.</returns>
    public int Flush(Span<float> output)
    {
        _isFlushing = true;
        var totalFlushed = 0;

        // Continue processing until output buffer is full or internal buffer can no longer yield a full window.
        while (totalFlushed < output.Length &&
               (_inputBufferValidSamples >= _windowSizeSamples))
        {
            var flushedThisCall = Process(ReadOnlySpan<float>.Empty, output.Slice(totalFlushed), out _,
                out _);
            if (flushedThisCall > 0) totalFlushed += flushedThisCall;
            else break;
        }

        _isFlushing = false;
        return totalFlushed;
    }
}