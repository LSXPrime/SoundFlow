using SoundFlow.Enums;
using SoundFlow.Interfaces;

namespace SoundFlow.Abstracts;

/// <summary>
/// Abstract base class for sound players, providing common functionality.
/// </summary>
public abstract class SoundPlayerBase : SoundComponent, ISoundPlayer
{
    private readonly ISoundDataProvider _dataProvider;
    private int _rawSamplePosition;
    private float _currentFractionalFrame;
    private float[] _resampleBuffer;
    private int _resampleBufferValidSamples;
    private float _playbackSpeed = 1.0f;
    private int _loopStartSamples;
    private int _loopEndSamples = -1;
    private bool _loopingSeekPending;
    private readonly WsolaTimeStretcher _timeStretcher;
    private readonly float[] _timeStretcherInputBuffer;
    private int _timeStretcherInputBufferValidSamples;
    private int _timeStretcherInputBufferReadOffset;

    /// <inheritdoc />
    public float PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Playback speed must be greater than zero.");
            if (Math.Abs(_playbackSpeed - value) > 1e-6f)
            {
                _playbackSpeed = value;
                _timeStretcher.SetSpeed(_playbackSpeed);
            }
        }
    }

    /// <inheritdoc />
    public PlaybackState State { get; private set; }

    /// <inheritdoc />
    public bool IsLooping { get; set; }

    /// <inheritdoc />
    public float Time =>
        _dataProvider.Length == 0 || AudioEngine.Channels == 0 || AudioEngine.Instance.SampleRate == 0
            ? 0
            : (float)_rawSamplePosition / AudioEngine.Channels / AudioEngine.Instance.SampleRate;

    /// <inheritdoc />
    public float Duration =>
        _dataProvider.Length == 0 || AudioEngine.Channels == 0 || AudioEngine.Instance.SampleRate == 0
            ? 0f
            : (float)_dataProvider.Length / AudioEngine.Channels / AudioEngine.Instance.SampleRate;

    /// <inheritdoc />
    public int LoopStartSamples => _loopStartSamples;

    /// <inheritdoc />
    public int LoopEndSamples => _loopEndSamples;

    /// <inheritdoc />
    public float LoopStartSeconds => (AudioEngine.Channels == 0 || AudioEngine.Instance.SampleRate == 0)
        ? 0
        : (float)_loopStartSamples / AudioEngine.Channels / AudioEngine.Instance.SampleRate;

    /// <inheritdoc />
    public float LoopEndSeconds =>
        _loopEndSamples == -1 || AudioEngine.Channels == 0 || AudioEngine.Instance.SampleRate == 0
            ? -1
            : (float)_loopEndSamples / AudioEngine.Channels / AudioEngine.Instance.SampleRate;


    /// <summary>
    /// Constructor for BaseSoundPlayer.
    /// </summary>
    /// <param name="dataProvider">The sound data provider.</param>
    /// <exception cref="ArgumentNullException">Thrown if dataProvider is null.</exception>
    protected SoundPlayerBase(ISoundDataProvider dataProvider)
    {
        _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        var initialChannels = AudioEngine.Channels > 0 ? AudioEngine.Channels : 2;
        var initialSampleRate = AudioEngine.Instance.SampleRate > 0 ? AudioEngine.Instance.SampleRate : 44100;
        var resampleBufferFrames = Math.Max(256, initialSampleRate / 10);
        _resampleBuffer = new float[resampleBufferFrames * initialChannels];
        _timeStretcher = new WsolaTimeStretcher(initialChannels, _playbackSpeed);
        _timeStretcherInputBuffer =
            new float[Math.Max(_timeStretcher.MinInputSamplesToProcess * 2, 8192 * initialChannels)];
    }

    /// <inheritdoc />
    protected override void GenerateAudio(Span<float> output)
    {
        // Clear output if not playing or no channels.
        if (State != PlaybackState.Playing || AudioEngine.Channels == 0)
        {
            output.Clear();
            return;
        }

        var channels = AudioEngine.Channels;
        // Ensure time stretcher has correct channel count.
        if (_timeStretcher.GetTargetSpeed() == 0f && _playbackSpeed != 0f && channels > 0)
            _timeStretcher.SetChannels(channels);

        if (channels == 0)
        {
            output.Clear();
            return;
        }

        var outputFramesTotal = output.Length / channels;
        var outputBufferOffset = 0;
        var totalSourceSamplesAdvancedThisCall = 0; // Total samples advanced in the original source.

        for (var i = 0; i < outputFramesTotal; i++)
        {
            var currentIntegerFrame = (int)Math.Floor(_currentFractionalFrame);
            // We need 2 frames for linear interpolation (current and next).
            var samplesRequiredInBufferForInterpolation = (currentIntegerFrame + 2) * channels;

            // Fill _resampleBuffer if not enough data for interpolation.
            if (_resampleBufferValidSamples < samplesRequiredInBufferForInterpolation)
            {
                var sourceSamplesForFill = FillResampleBuffer(samplesRequiredInBufferForInterpolation);
                totalSourceSamplesAdvancedThisCall += sourceSamplesForFill;

                // If still not enough data after filling, end of stream.
                if (_resampleBufferValidSamples < samplesRequiredInBufferForInterpolation)
                {
                    _rawSamplePosition += totalSourceSamplesAdvancedThisCall;
                    _rawSamplePosition = Math.Min(_rawSamplePosition, _dataProvider.Length);
                    HandleEndOfStream(output[outputBufferOffset..]);
                    return;
                }
            }

            // Perform linear interpolation.
            var frameIndex0 = currentIntegerFrame;
            var t = _currentFractionalFrame - frameIndex0;
            for (var ch = 0; ch < channels; ch++)
            {
                var sampleIndex0 = frameIndex0 * channels + ch;
                var sampleIndex1 = (frameIndex0 + 1) * channels + ch;
                if (sampleIndex1 >= _resampleBufferValidSamples)
                {
                    // If next sample is out of bounds, use current or 0.
                    output[outputBufferOffset + ch] =
                        (sampleIndex0 < _resampleBufferValidSamples && sampleIndex0 >= 0)
                            ? _resampleBuffer[sampleIndex0]
                            : 0f;
                    continue;
                }

 		// If current sample is out of bounds, use 0.
                if (sampleIndex0 < 0)
                {
                    output[outputBufferOffset + ch] = 0f;
                    continue;
                }

                // Interpolate sample value.
                output[outputBufferOffset + ch] =
                    _resampleBuffer[sampleIndex0] * (1.0f - t) + _resampleBuffer[sampleIndex1] * t;
            }

            outputBufferOffset += channels;
            _currentFractionalFrame += 1.0f;

            // Discard consumed samples from the resample buffer.
            var framesConsumedFromResampleBuffer = (int)Math.Floor(_currentFractionalFrame);
            if (framesConsumedFromResampleBuffer > 0)
            {
                var samplesConsumedFromResampleBuf = framesConsumedFromResampleBuffer * channels;

                var actualDiscard = Math.Min(samplesConsumedFromResampleBuf, _resampleBufferValidSamples);
                if (actualDiscard > 0)
                {
                    var remaining = _resampleBufferValidSamples - actualDiscard;
                    if (remaining > 0)
                        // Shift remaining samples to the beginning.
                        Buffer.BlockCopy(_resampleBuffer, actualDiscard * sizeof(float), _resampleBuffer, 0,
                            remaining * sizeof(float));
                    _resampleBufferValidSamples = remaining;
                }

                _currentFractionalFrame -= framesConsumedFromResampleBuffer;
            }
        }

        // Update raw sample position based on actual source samples advanced.
        _rawSamplePosition += totalSourceSamplesAdvancedThisCall;
        _rawSamplePosition = Math.Min(_rawSamplePosition, _dataProvider.Length);
    }

    /// <summary>
    /// Fills the internal resample buffer using the time stretcher and data provider.
    /// </summary>
    /// <param name="minSamplesRequiredInOutputBuffer">Minimum samples needed in _resampleBuffer.</param>
    /// <returns>The total number of original source samples advanced by this fill operation.</returns>
    private int FillResampleBuffer(int minSamplesRequiredInOutputBuffer)
    {
        var channels = AudioEngine.Channels;
        if (channels == 0) return 0;

        // Resize the resampling buffer if too small.
        if (_resampleBuffer.Length < minSamplesRequiredInOutputBuffer)
        {
            Array.Resize(ref _resampleBuffer,
                Math.Max(minSamplesRequiredInOutputBuffer, _resampleBuffer.Length * 2));
        }

        var totalSourceSamplesRepresented = 0;

        // Loop to fill _resampleBuffer until minimum required samples are met.
        while (_resampleBufferValidSamples < minSamplesRequiredInOutputBuffer)
        {
            var spaceAvailableInResampleBuffer = _resampleBuffer.Length - _resampleBufferValidSamples;
            if (spaceAvailableInResampleBuffer == 0) break;

            var availableInStretcherInput =
                _timeStretcherInputBufferValidSamples - _timeStretcherInputBufferReadOffset;
            var providerHasMoreData = _dataProvider.Position < _dataProvider.Length;

            // If time stretcher input buffer needs more data and provider has it.
            if (availableInStretcherInput < _timeStretcher.MinInputSamplesToProcess && providerHasMoreData)
            {
                // Shift existing valid data to the beginning of the input buffer.
                if (_timeStretcherInputBufferReadOffset > 0 && availableInStretcherInput > 0)
                {
                    Buffer.BlockCopy(_timeStretcherInputBuffer, _timeStretcherInputBufferReadOffset * sizeof(float),
                        _timeStretcherInputBuffer, 0, availableInStretcherInput * sizeof(float));
                }

                _timeStretcherInputBufferValidSamples = availableInStretcherInput;
                _timeStretcherInputBufferReadOffset = 0;

                // Read more data from the data provider into the time stretcher input buffer.
                var spaceToReadIntoInput = _timeStretcherInputBuffer.Length - _timeStretcherInputBufferValidSamples;
                if (spaceToReadIntoInput > 0)
                {
                    var readFromProvider = _dataProvider.ReadBytes(
                        _timeStretcherInputBuffer.AsSpan(_timeStretcherInputBufferValidSamples,
                            spaceToReadIntoInput));
                    _timeStretcherInputBufferValidSamples += readFromProvider;
                    availableInStretcherInput = _timeStretcherInputBufferValidSamples;
                    providerHasMoreData = _dataProvider.Position < _dataProvider.Length;
                }
            }

            // Prepare spans for time stretcher processing.
            var inputSpanForStretcher = ReadOnlySpan<float>.Empty;
            if (availableInStretcherInput > 0)
            {
                inputSpanForStretcher = _timeStretcherInputBuffer.AsSpan(_timeStretcherInputBufferReadOffset,
                    availableInStretcherInput);
            }

            var outputSpanForStretcher =
                _resampleBuffer.AsSpan(_resampleBufferValidSamples, spaceAvailableInResampleBuffer);
            int samplesWrittenToResample, samplesConsumedFromStretcherInputBuf, sourceSamplesForThisProcessCall;

            // Determine how to call the time stretcher (Process or Flush).
            if (inputSpanForStretcher.IsEmpty && !providerHasMoreData && !_loopingSeekPending)
            {
                samplesWrittenToResample = _timeStretcher.Flush(outputSpanForStretcher);
                samplesConsumedFromStretcherInputBuf = 0;
                sourceSamplesForThisProcessCall = 0;
            }
            else if (availableInStretcherInput >= _timeStretcher.MinInputSamplesToProcess ||
                     (inputSpanForStretcher.IsEmpty && providerHasMoreData && !_loopingSeekPending))
            {
 		// if input is empty but provider has more data, try to process what's already buffered.
                samplesWrittenToResample = _timeStretcher.Process(inputSpanForStretcher, outputSpanForStretcher,
                    out samplesConsumedFromStretcherInputBuf,
                    out sourceSamplesForThisProcessCall);
            }
            else if (_loopingSeekPending)
            {
                break;
            }
            else
            {
                break; // Not enough input and not flushing.
            }

            // Update read offset and valid samples for time stretcher input buffer.
            if (samplesConsumedFromStretcherInputBuf > 0)
            {
                _timeStretcherInputBufferReadOffset += samplesConsumedFromStretcherInputBuf;
            }

            // Update resample buffer valid samples and total source samples advanced.
            _resampleBufferValidSamples += samplesWrittenToResample;
            totalSourceSamplesRepresented += sourceSamplesForThisProcessCall;

            // Break if no progress was made and no more data is expected.
            if (samplesWrittenToResample == 0 && samplesConsumedFromStretcherInputBuf == 0 &&
                !providerHasMoreData && !_loopingSeekPending)
            {
                if (availableInStretcherInput ==
                    (_timeStretcherInputBufferValidSamples - _timeStretcherInputBufferReadOffset))
                {
                    break;
                }
            }
        }

        return totalSourceSamplesRepresented;
    }

    /// <summary>
    /// Handles the end-of-stream condition, including looping and stopping.
    /// </summary>
    protected virtual void HandleEndOfStream(Span<float> remainingOutputBuffer)
    {
        if (IsLooping)
        {
            var targetLoopStart = Math.Max(0, _loopStartSamples);
            var actualLoopEnd = (_loopEndSamples == -1)
                ? _dataProvider.Length
                : Math.Min(_loopEndSamples, _dataProvider.Length);

            if (targetLoopStart < actualLoopEnd && targetLoopStart < _dataProvider.Length)
            {
                _loopingSeekPending = true;
                Seek(targetLoopStart);
                _loopingSeekPending = false;
                if (!remainingOutputBuffer.IsEmpty)
                    GenerateAudio(remainingOutputBuffer);
                return;
            }
        }

        // If not looping or loop points are invalid, fill remaining buffer with what's left and stop.
        if (!remainingOutputBuffer.IsEmpty)
        {
            var spaceToFill = remainingOutputBuffer.Length;
            var currentlyValidInResample = _resampleBufferValidSamples;

            // Attempt one last fill of the resample buffer.
            if (currentlyValidInResample < spaceToFill)
            {
                var sourceSamplesFromFinalFill = FillResampleBuffer(Math.Max(currentlyValidInResample, spaceToFill));
                _rawSamplePosition += sourceSamplesFromFinalFill;
                _rawSamplePosition = Math.Min(_rawSamplePosition, _dataProvider.Length);
            }

            // Copy remaining valid samples to output and clear the rest.
            var toCopy = Math.Min(spaceToFill, _resampleBufferValidSamples);
            if (toCopy > 0)
            {
                _resampleBuffer.AsSpan(0, toCopy).CopyTo(remainingOutputBuffer.Slice(0, toCopy));
                var remainingInResampleAfterCopy = _resampleBufferValidSamples - toCopy;
                if (remainingInResampleAfterCopy > 0)
                {
                    // Shift remaining samples in resample buffer.
                    Buffer.BlockCopy(_resampleBuffer, toCopy * sizeof(float), _resampleBuffer, 0,
                        remainingInResampleAfterCopy * sizeof(float));
                }

                _resampleBufferValidSamples = remainingInResampleAfterCopy;
                if (toCopy < spaceToFill)
                {
                    remainingOutputBuffer.Slice(toCopy).Clear(); // Clear any unfilled part.
                }
            }
            else
            {
                remainingOutputBuffer.Clear(); // No valid samples, clear entire buffer.
            }
        }

        State = PlaybackState.Stopped;
        OnPlaybackEnded();
    }

    /// <summary>
    /// Invokes the PlaybackEnded event.
    /// </summary>
    protected virtual void OnPlaybackEnded()
    {
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
        var isEffectivelyLooping = IsLooping && (_loopEndSamples == -1 || _loopStartSamples < _loopEndSamples) &&
                                   _loopStartSamples < _dataProvider.Length;
        // If not effectively looping, disable the component.
        if (!isEffectivelyLooping) Enabled = false;
    }

    /// <summary>
    /// Occurs when playback ends.
    /// </summary>
    public event EventHandler<EventArgs>? PlaybackEnded;

    #region Audio Playback Control

    /// <inheritdoc />
    public void Play()
    {
        Enabled = true;
        State = PlaybackState.Playing;
    }

    /// <inheritdoc />
    public void Pause()
    {
        Enabled = false;
        State = PlaybackState.Paused;
    }

    /// <inheritdoc />
    public void Stop()
    {
        State = PlaybackState.Stopped;
        Enabled = false;
        Seek(0);
        _timeStretcher.Reset();
        _resampleBufferValidSamples = 0;
        Array.Clear(_resampleBuffer, 0, _resampleBuffer.Length);
        _timeStretcherInputBufferValidSamples = 0;
        _timeStretcherInputBufferReadOffset = 0;
        Array.Clear(_timeStretcherInputBuffer, 0, _timeStretcherInputBuffer.Length);
        _currentFractionalFrame = 0f;
    }

    /// <inheritdoc />
    public bool Seek(TimeSpan time, SeekOrigin seekOrigin = SeekOrigin.Begin)
    {
        if (AudioEngine.Channels == 0 || AudioEngine.Instance.SampleRate == 0) return false;
        float targetTimeSeconds;
        var currentDuration = Duration;
        switch (seekOrigin)
        {
            case SeekOrigin.Begin:
                targetTimeSeconds = (float)time.TotalSeconds;
                break;
            case SeekOrigin.Current: 
                targetTimeSeconds = Time + (float)time.TotalSeconds;
                break;
            case SeekOrigin.End:
                // If duration is 0, treat as seeking relative to 0.
                targetTimeSeconds = (currentDuration > 0 ? currentDuration : 0) + (float)time.TotalSeconds;
                break;
            default: return false;
        }

        // Clamp target time within valid duration.
        targetTimeSeconds = currentDuration > 0 ? Math.Clamp(targetTimeSeconds, 0, currentDuration) : Math.Max(0, targetTimeSeconds);
        return Seek(targetTimeSeconds);
    }

    /// <inheritdoc />
    public bool Seek(float timeInSeconds)
    {
        if (AudioEngine.Channels == 0 || AudioEngine.Instance.SampleRate == 0) return false;
        timeInSeconds = Math.Max(0, timeInSeconds);
        // Convert time in seconds to sample offset in source data.
        var sampleOffset = (int)(timeInSeconds / Duration * _dataProvider.Length);
        return Seek(sampleOffset);
    }

    /// <inheritdoc />
    public bool Seek(int sampleOffset)
    {
        if (!_dataProvider.CanSeek || AudioEngine.Channels == 0) return false;

        var maxSeekableSample = _dataProvider.Length > 0 ? _dataProvider.Length - AudioEngine.Channels : 0;
        maxSeekableSample = Math.Max(0, maxSeekableSample);
        // Align sample offset to frame boundary.
        sampleOffset = (sampleOffset / AudioEngine.Channels) * AudioEngine.Channels;
        sampleOffset = Math.Clamp(sampleOffset, 0, maxSeekableSample);
        _dataProvider.Seek(sampleOffset);
        _rawSamplePosition = sampleOffset;
        _currentFractionalFrame = 0f;
        _resampleBufferValidSamples = 0;
        _timeStretcher.Reset();
        _timeStretcherInputBufferValidSamples = 0;
        _timeStretcherInputBufferReadOffset = 0;
        return true;
    }

    #endregion

    #region Loop Point Configuration Methods

    /// <inheritdoc />
    public void SetLoopPoints(float startTime, float? endTime = null)
    {
        if (AudioEngine.Channels == 0 || AudioEngine.Instance.SampleRate == 0) return;

        if (startTime < 0)
            throw new ArgumentOutOfRangeException(nameof(startTime), "Loop start time cannot be negative.");

        var effectiveEndTime = endTime ?? -1f;
        if (Math.Abs(effectiveEndTime - -1f) > 1e-6f && effectiveEndTime < startTime)
            throw new ArgumentOutOfRangeException(nameof(endTime),
                "Loop end time must be greater than or equal to start time, or -1.");

        // Convert seconds to samples.
        _loopStartSamples = (int)(startTime * AudioEngine.Instance.SampleRate * AudioEngine.Channels);
        _loopEndSamples = Math.Abs(effectiveEndTime - -1f) < 1e-6f
            ? -1
            : (int)(effectiveEndTime * AudioEngine.Instance.SampleRate * AudioEngine.Channels);

        // Align to frame boundaries and clamp within data provider length.
        _loopStartSamples = (_loopStartSamples / AudioEngine.Channels) * AudioEngine.Channels;
        _loopStartSamples = Math.Clamp(_loopStartSamples, 0, _dataProvider.Length);

        if (_loopEndSamples != -1)
        {
            _loopEndSamples = _loopEndSamples / AudioEngine.Channels * AudioEngine.Channels;
            _loopEndSamples = Math.Clamp(_loopEndSamples, _loopStartSamples, _dataProvider.Length);
        }
    }

    /// <inheritdoc />
    public void SetLoopPoints(int startSample, int endSample = -1)
    {
        if (AudioEngine.Channels == 0) return;

        if (startSample < 0)
            throw new ArgumentOutOfRangeException(nameof(startSample), "Loop start sample cannot be negative.");
        if (endSample != -1 && endSample < startSample)
            throw new ArgumentOutOfRangeException(nameof(endSample),
                "Loop end sample must be greater than or equal to start sample, or -1.");

        // Align to frame boundaries and clamp.
        _loopStartSamples = (startSample / AudioEngine.Channels) * AudioEngine.Channels;
        _loopStartSamples = Math.Clamp(_loopStartSamples, 0, _dataProvider.Length);

        if (endSample != -1)
        {
            endSample = Math.Max(startSample, endSample);
            _loopEndSamples = (endSample / AudioEngine.Channels) * AudioEngine.Channels;
            _loopEndSamples = Math.Clamp(_loopEndSamples, _loopStartSamples, _dataProvider.Length);
        }
        else
        {
            _loopEndSamples = -1;
        }
    }

    /// <inheritdoc />
    public void SetLoopPoints(TimeSpan startTime, TimeSpan? endTime = null)
    {
        SetLoopPoints((float)startTime.TotalSeconds, (float?)endTime?.TotalSeconds);
    }

    #endregion
}

/// <summary>
/// Implements the WSOLA (Waveform Similarity Overlap-Add) algorithm for real-time time stretching
/// and pitch preservation of audio. It allows changing playback speed without altering pitch.
/// </summary>
public class WsolaTimeStretcher
{
    public int Channels { get; private set; }
    private float _speed = 1.0f;

    public const int DefaultWindowSizeFrames = 1024;
    private const int NominalAnalysisHopFrames = DefaultWindowSizeFrames / 4;
    private const int SearchRadiusFrames = (NominalAnalysisHopFrames * 3) / 8;

    private int _windowSizeSamples;
    private float[] _inputBufferInternal;
    private int _inputBufferValidSamples = 0;
    private int _inputBufferReadPos = 0;
    private float[] _analysisWindow;
    private float[] _prevOutputTail;
    private int _actualPrevTailLength = 0;
    private float[] _currentAnalysisFrame;
    private float[] _outputOverlapBuffer;
    private int _nominalHopSynthesisFrames;
    private bool _isFirstFrame = true;
    private bool _isFlushing = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="WsolaTimeStretcher"/> class.
    /// </summary>
    /// <param name="initialChannels">The initial number of audio channels. Defaults to 2 if not positive.</param>
    /// <param name="initialSpeed">The initial playback speed. Defaults to 1.0f.</param>
    public WsolaTimeStretcher(int initialChannels = 2, float initialSpeed = 1.0f)
    {
        initialChannels = initialChannels switch
        {
            <= 0 when AudioEngine.Channels > 0 => AudioEngine.Channels,
            <= 0 => 2,
            _ => initialChannels
        };
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
        if (Channels == channels) return;
        Channels = channels;
        _windowSizeSamples = DefaultWindowSizeFrames * Channels;
        _prevOutputTail = new float[Math.Max(Channels, _windowSizeSamples - Channels)];
        const int maxInputReachFrames = NominalAnalysisHopFrames + SearchRadiusFrames + DefaultWindowSizeFrames;
        _inputBufferInternal = new float[maxInputReachFrames * Channels * 2];
        // Initialize Hann window for smooth fading.
        _analysisWindow = new float[DefaultWindowSizeFrames];
        for (var i = 0; i < DefaultWindowSizeFrames; i++)
            _analysisWindow[i] = 0.5f * (1 - (float)Math.Cos(2 * Math.PI * i / (DefaultWindowSizeFrames - 1)));
        _currentAnalysisFrame = new float[_windowSizeSamples];
        _outputOverlapBuffer = new float[_windowSizeSamples];
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
    }

    /// <summary>
    /// Gets the minimum number of input samples required in the internal buffer to perform a processing step.
    /// </summary>
    public int MinInputSamplesToProcess =>
        (NominalAnalysisHopFrames + SearchRadiusFrames) * Channels + _windowSizeSamples;

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
        if (Channels == 0 || output.IsEmpty) return 0;

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
                var synthesisHopSamples = _nominalHopSynthesisFrames * Channels;
                // Length of the overlap region to compare.
                var compareLengthSamples =
                    Math.Min(_actualPrevTailLength, _windowSizeSamples - synthesisHopSamples);
                compareLengthSamples = Math.Max(0, compareLengthSamples);
                var compareLengthFrames = compareLengthSamples / Channels;
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
                    var candidateStartAtDelta0 = _inputBufferReadPos + NominalAnalysisHopFrames * Channels;
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
                        var candidateSegmentStartSample = _inputBufferReadPos + trialAnalysisHopFrames * Channels;
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
                actualAnalysisHopFrames * Channels;

            // Calculate the starting position of the chosen analysis segment in the input buffer.
            var chosenSegmentStartSampleInInput = _inputBufferReadPos +
                                                  (NominalAnalysisHopFrames + bestOffsetFromNominalFrames) * Channels;

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
                for (var ch = 0; ch < Channels; ch++)
                {
                    var readIdx = chosenSegmentStartSampleInInput + f * Channels + ch;
                    _currentAnalysisFrame[f * Channels + ch] = _inputBufferInternal[readIdx] * _analysisWindow[f];
                }
            }

            // Perform overlap-add synthesis.
            var currentFrameSynthesisHopSamples = _nominalHopSynthesisFrames * Channels;
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
                    ? (int)Math.Round((double)actualCopyToOutput / (_nominalHopSynthesisFrames * Channels) * actualAnalysisHopSamples)
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
            if (_isFlushing && (_inputBufferValidSamples - _inputBufferReadPos < Channels)) break;
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