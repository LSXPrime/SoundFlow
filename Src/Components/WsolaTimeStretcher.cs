namespace SoundFlow.Components;

/// <summary>
/// Implements the WSOLA (Waveform Similarity Overlap-Add) algorithm for real-time time stretching
/// and pitch preservation of audio. It allows changing playback speed without altering pitch.
/// </summary>
public class WsolaTimeStretcher
{
    private int _channels;
    private float _speed = 1.0f;

    internal const int DefaultWindowSizeFrames = 1024;
    private const int NominalAnalysisHopFrames = DefaultWindowSizeFrames / 4;
    private const int SearchRadiusFrames = (NominalAnalysisHopFrames * 3) / 8;

    private int _windowSizeSamples;
    private float[] _inputBufferInternal = [];
    private int _inputBufferValidSamples;
    private int _inputBufferReadPos;
    private float[] _analysisWindow = [];
    private float[] _prevOutputTail = [];
    private int _actualPrevTailLength;
    private float[] _currentAnalysisFrame = [];
    private float[] _outputOverlapBuffer = [];
    private int _nominalHopSynthesisFrames;
    private bool _isFirstFrame = true;
    private bool _isFlushing;

    /// <summary>
    /// Initializes a new instance of the <see cref="WsolaTimeStretcher"/> class.
    /// </summary>
    /// <param name="initialChannels">The initial number of audio channels. Defaults to 2 if not positive.</param>
    /// <param name="initialSpeed">The initial playback speed. Defaults to 1.0f.</param>
    public WsolaTimeStretcher(int initialChannels = 2, float initialSpeed = 1.0f)
    {
        initialChannels = initialChannels <= 0 ? 2 : initialChannels;
        SetChannels(initialChannels);
        SetSpeed(initialSpeed);
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
        _windowSizeSamples = DefaultWindowSizeFrames * _channels;
        _prevOutputTail = new float[Math.Max(_channels, _windowSizeSamples - _channels)];
        const int maxInputReachFrames = NominalAnalysisHopFrames + SearchRadiusFrames + DefaultWindowSizeFrames;
        _inputBufferInternal = new float[maxInputReachFrames * _channels * 2];
        // Initialize Hann window for smooth fading.
        _analysisWindow = new float[DefaultWindowSizeFrames];
        for (var i = 0; i < DefaultWindowSizeFrames; i++)
        {
            _analysisWindow[i] = 0.5f * (1 - (float)Math.Cos(2 * Math.PI * i / (DefaultWindowSizeFrames - 1)));
        }
        _currentAnalysisFrame = new float[_windowSizeSamples];

        // Ensure the overlap buffer is large enough for the current speed's hop size
        var requiredBufferSize = Math.Max(_windowSizeSamples, _nominalHopSynthesisFrames * _channels);
        _outputOverlapBuffer = new float[requiredBufferSize];
        
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
        // Calculate nominal synthesis hop frames based on the inverse of the speed.
        _nominalHopSynthesisFrames = (int)Math.Max(1, Math.Round(NominalAnalysisHopFrames / _speed));

        //  Resize buffer if the new hop size exceeds the window size.
        if (_channels > 0)
        {
            var requiredBufferSize = Math.Max(_windowSizeSamples, _nominalHopSynthesisFrames * _channels);
            if (_outputOverlapBuffer.Length < requiredBufferSize)
            {
                _outputOverlapBuffer = new float[requiredBufferSize];
            }
        }
    }

    /// <summary>
    /// Gets the minimum number of input samples required in the internal buffer to perform a processing step.
    /// </summary>
    public int MinInputSamplesToProcess =>
        (NominalAnalysisHopFrames + SearchRadiusFrames) * _channels + _windowSizeSamples;

    /// <summary>
    /// Resets the internal state of the time stretcher, clearing all buffers and flags.
    /// This should be called when seeking or stopping playback.
    /// </summary>
    private void ResetState()
    {
        _inputBufferValidSamples = 0;
        _inputBufferReadPos = 0;
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
    /// Processes a segment of audio data for time stretching.
    /// </summary>
    /// <param name="input">The input audio data to be processed.</param>
    /// <param name="output">The span to write the processed audio data to.</param>
    /// <param name="samplesConsumedFromInputBuffer">Output parameter: The number of samples consumed from the input span.</param>
    /// <param name="sourceSamplesRepresentedByOutput">Output parameter: The number of *original* source samples that the generated output represents.</param>
    /// <returns>The number of samples written to the output span.</returns>
    public int Process(ReadOnlySpan<float> input, Span<float> output,
        out int samplesConsumedFromInputBuffer,
        out int sourceSamplesRepresentedByOutput)
    {
        samplesConsumedFromInputBuffer = 0;
        sourceSamplesRepresentedByOutput = 0;
        if (_channels == 0 || output.IsEmpty) return 0;

        // Copy incoming input data into the internal buffer, shifting existing data if necessary.
        if (!input.IsEmpty)
        {
            if (_inputBufferReadPos > 0 && _inputBufferValidSamples > _inputBufferReadPos)
                Buffer.BlockCopy(_inputBufferInternal, _inputBufferReadPos * sizeof(float), _inputBufferInternal, 0,
                    (_inputBufferValidSamples - _inputBufferReadPos) * sizeof(float));
            _inputBufferValidSamples -= _inputBufferReadPos;
            _inputBufferReadPos = 0;
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

        // Loop to generate as much output as possible given available input and output buffer space.
        while (samplesWrittenToOutput < output.Length)
        {
            // Check if enough input samples are available to process a full window + search area.
            if (_inputBufferValidSamples - _inputBufferReadPos < MinInputSamplesToProcess)
            {
                // If not flushing and not enough data, shift remaining data and return.
                if (!_isFlushing || (_inputBufferValidSamples - _inputBufferReadPos < _windowSizeSamples))
                {
                    if (_inputBufferReadPos > 0 && _inputBufferValidSamples > _inputBufferReadPos)
                        Buffer.BlockCopy(_inputBufferInternal, _inputBufferReadPos * sizeof(float),
                            _inputBufferInternal, 0,
                            (_inputBufferValidSamples - _inputBufferReadPos) * sizeof(float));
                    _inputBufferValidSamples -= _inputBufferReadPos;
                    _inputBufferReadPos = 0;
                    sourceSamplesRepresentedByOutput = totalSourceSamplesForThisCall;
                    return samplesWrittenToOutput;
                }
            }

            var bestOffsetFromNominalFrames = 0;

            // If not the first frame and a previous output tail exists, perform WSOLA's search for best overlap.
            if (!_isFirstFrame && _actualPrevTailLength > 0)
            {
                var synthesisHopSamples = _nominalHopSynthesisFrames * _channels;
                // Length of the overlap region to compare.
                var compareLengthSamples =
                    Math.Min(_actualPrevTailLength, _windowSizeSamples - synthesisHopSamples);
                compareLengthSamples = Math.Max(0, compareLengthSamples);
                var compareLengthFrames = compareLengthSamples / _channels;
                const int minValidOverlapForSearch = SearchRadiusFrames / 4;
                float prevTailEnergy = 0;
                if (compareLengthFrames > 0)
                {
                    for (var iS = 0; iS < compareLengthSamples; ++iS)
                        prevTailEnergy += _prevOutputTail[iS] * _prevOutputTail[iS];
                }

                var silenceThreshold = 1e-7f * compareLengthSamples; // Threshold to avoid correlation on silence.

                // Only perform search if previous tail has significant energy and enough length for meaningful correlation.
                if (prevTailEnergy > silenceThreshold && compareLengthFrames > minValidOverlapForSearch &&
                    compareLengthSamples > 0)
                {
                    var maxNcc = -2.0;
                    // Calculate mean and sum of squared deviations for the previous output tail (A).
                    double sumA = 0;
                    for (var iS = 0; iS < compareLengthSamples; ++iS) sumA += _prevOutputTail[iS];
                    var meanA = sumA / compareLengthSamples;
                    double sumADevSq = 0;
                    for (var iS = 0; iS < compareLengthSamples; ++iS)
                    {
                        var d = _prevOutputTail[iS] - meanA;
                        sumADevSq += d * d;
                    }

                    // Pre-calculate NCC for delta 0 (nominal hop) as a baseline.
                    var candidateStartAtDelta0 = _inputBufferReadPos + NominalAnalysisHopFrames * _channels;
                    if (NominalAnalysisHopFrames > 0 &&
                        (candidateStartAtDelta0 + compareLengthSamples <= _inputBufferValidSamples))
                    {
                        double sumBd0 = 0;
                        for (var iS = 0; iS < compareLengthSamples; ++iS)
                            sumBd0 += _inputBufferInternal[candidateStartAtDelta0 + iS];
                        var meanBd0 = sumBd0 / compareLengthSamples;
                        double sumBDevSqD0 = 0, dotProductDevD0 = 0;
                        for (var iS = 0; iS < compareLengthSamples; ++iS)
                        {
                            var dA = _prevOutputTail[iS] - meanA;
                            var dB = _inputBufferInternal[candidateStartAtDelta0 + iS] - meanBd0;
                            dotProductDevD0 += dA * dB;
                            sumBDevSqD0 += dB * dB;
                        }

                        var denominatorD0 = Math.Sqrt(sumADevSq * sumBDevSqD0);
                        if (denominatorD0 < 1e-9)
                            maxNcc = (sumADevSq < 1e-9 && sumBDevSqD0 < 1e-9) ? 1.0 : 0.0;
                        else maxNcc = dotProductDevD0 / denominatorD0;
                    }

                    // Iterate through search radius to find the best overlap.
                    for (var currentDeltaFrames = -SearchRadiusFrames;
                         currentDeltaFrames <= SearchRadiusFrames;
                         currentDeltaFrames++)
                    {
                        if (currentDeltaFrames == 0) continue;
                        var trialAnalysisHopFrames = NominalAnalysisHopFrames + currentDeltaFrames;
                        if (trialAnalysisHopFrames <= 0) continue;
                        var candidateSegmentStartSample = _inputBufferReadPos + trialAnalysisHopFrames * _channels;
                        // Check if candidate segment is within valid input data.
                        if (candidateSegmentStartSample + compareLengthSamples > _inputBufferValidSamples)
                        {
                            if (currentDeltaFrames > 0) break;
                            continue;
                        }

                        // Calculate mean and sum of squared deviations for the current candidate segment (B).
                        double sumB = 0;
                        for (var iS = 0; iS < compareLengthSamples; ++iS)
                            sumB += _inputBufferInternal[candidateSegmentStartSample + iS];
                        var meanB = sumB / compareLengthSamples;
                        double sumBDevSq = 0, dotProductDev = 0;
                        for (var iS = 0; iS < compareLengthSamples; ++iS)
                        {
                            var dA = _prevOutputTail[iS] - meanA;
                            var dB = _inputBufferInternal[candidateSegmentStartSample + iS] - meanB;
                            dotProductDev += dA * dB;
                            sumBDevSq += dB * dB;
                        }

                        // Calculate Normalized Cross-Correlation (NCC).
                        double currentNcc;
                        var denominator = Math.Sqrt(sumADevSq * sumBDevSq);
                        if (denominator < 1e-9) currentNcc = (sumADevSq < 1e-9 && sumBDevSq < 1e-9) ? 1.0 : 0.0;
                        else currentNcc = dotProductDev / denominator;
                        const float nccQualityThreshold = 0.02f;
                        if (currentNcc > maxNcc + nccQualityThreshold)
                        {
                            maxNcc = currentNcc;
                            bestOffsetFromNominalFrames = currentDeltaFrames;
                        }
                        else if (currentNcc > maxNcc - nccQualityThreshold)
                        {
                            if (Math.Abs(currentDeltaFrames) < Math.Abs(bestOffsetFromNominalFrames))
                            {
                                maxNcc = currentNcc;
                                bestOffsetFromNominalFrames = currentDeltaFrames;
                            }
                        }
                    }
                }
            }

            // Determine the actual analysis hop based on the best overlap found.
            var actualAnalysisHopFrames = NominalAnalysisHopFrames + bestOffsetFromNominalFrames;
            if (actualAnalysisHopFrames <= 0) actualAnalysisHopFrames = 1;
            var actualAnalysisHopSamples =
                actualAnalysisHopFrames * _channels;

            // Calculate the starting position of the chosen analysis segment in the input buffer.
            var chosenSegmentStartSampleInInput = _inputBufferReadPos +
                                                  (NominalAnalysisHopFrames + bestOffsetFromNominalFrames) * _channels;

            // Check if the chosen segment is within valid input data. If not, handle end of input.
            if (chosenSegmentStartSampleInInput + _windowSizeSamples > _inputBufferValidSamples)
            {
                if (_isFlushing)
                {
                    // If flushing, try to use the last possible full window.
                    chosenSegmentStartSampleInInput = _inputBufferValidSamples - _windowSizeSamples;
                    if (chosenSegmentStartSampleInInput < _inputBufferReadPos)
                    {
                        // If even the last full window is beyond current read position, shift buffer and return.
                        if (_inputBufferReadPos > 0 && _inputBufferValidSamples > _inputBufferReadPos)
                        {
                            Buffer.BlockCopy(_inputBufferInternal, _inputBufferReadPos * sizeof(float),
                                _inputBufferInternal, 0,
                                (_inputBufferValidSamples - _inputBufferReadPos) * sizeof(float));
                            _inputBufferValidSamples -= _inputBufferReadPos;
                            _inputBufferReadPos = 0;
                        }
                        sourceSamplesRepresentedByOutput = totalSourceSamplesForThisCall;
                        return samplesWrittenToOutput;
                    }
                }
                else
                {
                    // If not flushing and not enough data, shift buffer and return.
                    if (_inputBufferReadPos > 0 && _inputBufferValidSamples > _inputBufferReadPos)
                    {
                        Buffer.BlockCopy(_inputBufferInternal, _inputBufferReadPos * sizeof(float),
                            _inputBufferInternal, 0,
                            (_inputBufferValidSamples - _inputBufferReadPos) * sizeof(float));
                        _inputBufferValidSamples -= _inputBufferReadPos;
                        _inputBufferReadPos = 0;
                    }
                    sourceSamplesRepresentedByOutput = totalSourceSamplesForThisCall;
                    return samplesWrittenToOutput;
                }
            }

            // Apply the analysis window to the chosen input segment.
            for (var f = 0; f < DefaultWindowSizeFrames; f++)
            {
                for (var ch = 0; ch < _channels; ch++)
                {
                    var readIdx = chosenSegmentStartSampleInInput + f * _channels + ch;
                    _currentAnalysisFrame[f * _channels + ch] = _inputBufferInternal[readIdx] * _analysisWindow[f];
                }
            }

            // Perform overlap-add synthesis.
            var currentFrameSynthesisHopSamples = _nominalHopSynthesisFrames * _channels;
            var currentFrameSynthesisOverlapSamples = Math.Max(0, _windowSizeSamples - currentFrameSynthesisHopSamples);
            Array.Clear(_outputOverlapBuffer, 0, _outputOverlapBuffer.Length);

            // Add previous output tail (overlap part) to the output overlap buffer.
            if (!_isFirstFrame && _actualPrevTailLength > 0)
            {
                var overlapToUseFromPrev = Math.Min(_actualPrevTailLength, currentFrameSynthesisOverlapSamples);
                if (overlapToUseFromPrev > 0)
                    _prevOutputTail.AsSpan(0, overlapToUseFromPrev)
                        .CopyTo(_outputOverlapBuffer.AsSpan(0, overlapToUseFromPrev));
            }

            // Add the current analysis frame (windowed input segment) to the output overlap buffer.
            for (var i = 0; i < _windowSizeSamples; ++i)
            {
                if (!_isFirstFrame && _actualPrevTailLength > 0 &&
                    i < Math.Min(_actualPrevTailLength, currentFrameSynthesisOverlapSamples))
                    _outputOverlapBuffer[i] += _currentAnalysisFrame[i];
                else _outputOverlapBuffer[i] = _currentAnalysisFrame[i];
            }

            // Copy the synthesized output segment (non-overlapping part) to the external output span.
            var availableInOutputSpan = output.Length - samplesWrittenToOutput;
            var actualCopyToOutput = Math.Min(currentFrameSynthesisHopSamples, availableInOutputSpan);

            if (actualCopyToOutput > 0)
            {
                _outputOverlapBuffer.AsSpan(0, actualCopyToOutput).CopyTo(output.Slice(samplesWrittenToOutput));
                samplesWrittenToOutput += actualCopyToOutput;

                // Estimate source samples represented by the output. This is proportional to the hop sizes.
                totalSourceSamplesForThisCall += _nominalHopSynthesisFrames > 0
                    ? (int)Math.Round((double)actualCopyToOutput / (_nominalHopSynthesisFrames * _channels) * actualAnalysisHopSamples)
                    : actualAnalysisHopSamples;
            }

            // Store the tail of the current synthesis frame for the next overlap-add step.
            switch (currentFrameSynthesisOverlapSamples)
            {
                case > 0 when
                    _prevOutputTail.Length >= currentFrameSynthesisOverlapSamples:
                    Array.Copy(_outputOverlapBuffer, currentFrameSynthesisHopSamples, _prevOutputTail, 0,
                        currentFrameSynthesisOverlapSamples);
                    break;
                case > 0:
                    // If _prevOutputTail is too small, clear it to avoid issues (something went off).
                    Array.Clear(_prevOutputTail, 0, _prevOutputTail.Length);
                    break;
            }

            _actualPrevTailLength = currentFrameSynthesisOverlapSamples;
            _isFirstFrame = false;
            _inputBufferReadPos += actualAnalysisHopSamples;
            
            if (samplesWrittenToOutput >= output.Length) break;
            if (_isFlushing && (_inputBufferValidSamples - _inputBufferReadPos < _channels)) break;
        }

        // Shift any remaining data in the internal input buffer to the beginning.
        var remainingInternalInput = _inputBufferValidSamples - _inputBufferReadPos;
        if (_inputBufferReadPos > 0 && remainingInternalInput > 0)
            Buffer.BlockCopy(_inputBufferInternal, _inputBufferReadPos * sizeof(float), _inputBufferInternal, 0,
                remainingInternalInput * sizeof(float));
        _inputBufferValidSamples = remainingInternalInput;
        _inputBufferReadPos = 0;

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
               (_inputBufferValidSamples - _inputBufferReadPos >= _windowSizeSamples))
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
