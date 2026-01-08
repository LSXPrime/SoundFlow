using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Structs;

namespace SoundFlow.Abstracts;

/// <summary>
/// Abstract base class for sound players, providing common functionality.
/// </summary>
public abstract class SoundPlayerBase : SoundComponent, ISoundPlayer
{
    private int _rawSamplePosition;

    private readonly ISoundDataProvider _dataProvider;
    private float _currentFractionalFrame;
    private float[] _resampleBuffer;
    private int _resampleBufferValidSamples;
    private float _playbackSpeed = 1.0f;
    private int _loopStartSamples;
    private int _loopEndSamples = -1;
    private readonly WsolaTimeStretcher _timeStretcher;
    private float[] _timeStretcherInputBuffer;
    private int _timeStretcherInputBufferValidSamples;
    private int _timeStretcherInputBufferReadOffset;
    private WsolaConfig _timeStretchConfig;

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

    /// <summary>
    /// Gets or sets the detailed configuration for the WSOLA time stretcher.
    /// Allows fine-tuning of window size, hop size, and search radius.
    /// </summary>
    public WsolaConfig TimeStretchConfig
    {
        get => _timeStretchConfig;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_timeStretchConfig != value)
            {
                _timeStretchConfig = value;
                _timeStretcher.Configure(value);
                EnsureTimeStretcherBufferSize();
            }
        }
    }

    /// <summary>
    /// Sets the time stretcher configuration based on a performance preset.
    /// </summary>
    /// <param name="preset">The desired balance between quality and performance.</param>
    public void SetTimeStretchQuality(WsolaPerformancePreset preset)
    {
        TimeStretchConfig = WsolaConfig.FromPreset(preset);
    }

    /// <inheritdoc />
    public PlaybackState State { get; internal set; }

    /// <inheritdoc />
    public ISoundDataProvider DataProvider => _dataProvider;

    /// <inheritdoc />
    public bool IsLooping { get; set; }

    /// <inheritdoc />
    public float Time =>
        _dataProvider.Length == 0 || Format.Channels == 0 || Format.SampleRate == 0
            ? 0
            : (float)_rawSamplePosition / Format.Channels / Format.SampleRate;

    /// <inheritdoc />
    public float Duration =>
        _dataProvider.Length == 0 || Format.Channels == 0 || Format.SampleRate == 0
            ? 0f
            : (float)_dataProvider.Length / Format.Channels / Format.SampleRate;

    /// <inheritdoc />
    public int LoopStartSamples => _loopStartSamples;

    /// <inheritdoc />
    public int LoopEndSamples => _loopEndSamples;

    /// <inheritdoc />
    public float LoopStartSeconds => (Format.Channels == 0 || Format.SampleRate == 0)
        ? 0
        : (float)_loopStartSamples / Format.Channels / Format.SampleRate;

    /// <inheritdoc />
    public float LoopEndSeconds =>
        _loopEndSamples == -1 || Format.Channels == 0 || Format.SampleRate == 0
            ? -1
            : (float)_loopEndSamples / Format.Channels / Format.SampleRate;


    /// <summary>
    /// Constructor for Base SoundPlayer.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="format">The audio device instance.</param>
    /// <param name="dataProvider">The sound data provider.</param>
    /// <exception cref="ArgumentNullException">Thrown if dataProvider is null.</exception>
    protected SoundPlayerBase(AudioEngine engine, AudioFormat format, ISoundDataProvider dataProvider) : base(engine, format)
    {
        _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        var initialChannels = format.Channels > 0 ? format.Channels : 2;
        var initialSampleRate = format.SampleRate > 0 ? format.SampleRate : 44100;
        var resampleBufferFrames = Math.Max(256, initialSampleRate / 10);
        _resampleBuffer = new float[resampleBufferFrames * initialChannels];
        
        // Initialize with default fast config
        _timeStretchConfig = WsolaConfig.FromPreset(WsolaPerformancePreset.Fast);
        _timeStretcher = new WsolaTimeStretcher(initialChannels, _playbackSpeed, _timeStretchConfig);
        _timeStretcherInputBuffer = new float[Math.Max(_timeStretcher.MinInputSamplesToProcess * 2, 8192 * initialChannels)];
    }

    /// <inheritdoc />
    protected override void GenerateAudio(Span<float> output, int channels)
    {
        // Clear output if not playing or no channels.
        if (State != PlaybackState.Playing || channels == 0)
        {
            output.Clear();
            return;
        }
        
        // Proactively check for looping before generating audio. This handles loops where a specific end point is set.
        if (IsLooping && _loopEndSamples != -1)
        {
            // Ensure loop is valid, and we've reached or passed the end point.
            if (_loopStartSamples < _loopEndSamples && _rawSamplePosition >= _loopEndSamples)
                Seek(_loopStartSamples, channels);
        }

        // Directly read from provider when playback speed is 1.0
        if (Math.Abs(_playbackSpeed - 1.0f) < 0.001f)
        {
            var outputSlice = output;
            
            // Loop until the output buffer is full or the stream truly ends.
            while (!outputSlice.IsEmpty)
            {
                var samplesReadThisCall = _dataProvider.ReadBytes(outputSlice);
                
                // A return value of 0 is the ONLY reliable end-of-stream signal.
                if (samplesReadThisCall == 0)
                {
                    // The data provider is exhausted. Handle the end of stream for the remaining part of the buffer.
                    HandleEndOfStream(outputSlice, channels);
                    break; // Exit the read loop
                }

                _rawSamplePosition += samplesReadThisCall;
                outputSlice = outputSlice.Slice(samplesReadThisCall);
            }
            return;
        }
        
        // Ensure time stretcher has correct channel count.
        if (_timeStretcher.GetTargetSpeed() == 0f && _playbackSpeed != 0f && channels > 0)
            _timeStretcher.SetChannels(channels);

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
                var sourceSamplesForFill = FillResampleBuffer(samplesRequiredInBufferForInterpolation, channels);
                totalSourceSamplesAdvancedThisCall += sourceSamplesForFill;

                // If still not enough data after filling and the provider is truly exhausted and can't provide more data, end of stream.
                if (_resampleBufferValidSamples < samplesRequiredInBufferForInterpolation)
                {
                    _rawSamplePosition += totalSourceSamplesAdvancedThisCall;
                    _rawSamplePosition = Math.Min(_rawSamplePosition, _dataProvider.Length);
                    HandleEndOfStream(output[outputBufferOffset..], channels);
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
                    output[outputBufferOffset + ch] = (sampleIndex0 < _resampleBufferValidSamples && sampleIndex0 >= 0) ? _resampleBuffer[sampleIndex0] : 0f;
                    continue;
                }
                if (sampleIndex0 < 0)
                {
                    output[outputBufferOffset + ch] = 0f;
                    continue;
                }
                // Interpolate sample value.
                output[outputBufferOffset + ch] = _resampleBuffer[sampleIndex0] * (1.0f - t) + _resampleBuffer[sampleIndex1] * t;
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
                    if (remaining > 0) // Shift remaining samples to the beginning.
                        Buffer.BlockCopy(_resampleBuffer, actualDiscard * sizeof(float), _resampleBuffer, 0, remaining * sizeof(float));
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
    /// <param name="channels">The number of channels to process.</param>
    /// <returns>The total number of original source samples advanced by this fill operation.</returns>
    private int FillResampleBuffer(int minSamplesRequiredInOutputBuffer, int channels)
    {
        if (channels == 0) return 0;

        // Resize the resampling buffer if too small.
        if (_resampleBuffer.Length < minSamplesRequiredInOutputBuffer)
        {
            Array.Resize(ref _resampleBuffer,
                Math.Max(minSamplesRequiredInOutputBuffer, _resampleBuffer.Length * 2));
        }

        // When playback speed is close to 1.0, use simpler interpolation.
        if (Math.Abs(_playbackSpeed - 1.0f) < 0.001f)
        {
            // Implement a persistent read loop instead of a single read call.
            var totalDirectRead = 0;
            var directReadSlice = _resampleBuffer.AsSpan(_resampleBufferValidSamples);

            while (!directReadSlice.IsEmpty)
            {
                var readThisCall = _dataProvider.ReadBytes(directReadSlice);
                if (readThisCall == 0)
                {
                    break; // True end of stream.
                }
                totalDirectRead += readThisCall;
                directReadSlice = directReadSlice.Slice(readThisCall);
            }

            _resampleBufferValidSamples += totalDirectRead;
            return totalDirectRead;
        }

        var totalSourceSamplesRepresented = 0;

        // Loop to fill _resampleBuffer until minimum required samples are met.
        while (_resampleBufferValidSamples < minSamplesRequiredInOutputBuffer)
        {
            var spaceAvailableInResampleBuffer = _resampleBuffer.Length - _resampleBufferValidSamples;
            if (spaceAvailableInResampleBuffer == 0) break;

            var availableInStretcherInput =
                _timeStretcherInputBufferValidSamples - _timeStretcherInputBufferReadOffset;

            // Use a flag to track the true end of the data provider.
            var providerExhausted = false;

            // If time stretcher input buffer needs more data.
            if (availableInStretcherInput < _timeStretcher.MinInputSamplesToProcess)
            {
                // Compact the buffer by moving the remaining valid samples to the start if we have a read offset.
                if (_timeStretcherInputBufferReadOffset > 0)
                {
                    // Calculate remaining samples. It should not be negative, but we defend against it.
                    var remaining = _timeStretcherInputBufferValidSamples - _timeStretcherInputBufferReadOffset;
                    if (remaining > 0)
                    {
                        // Shift the remaining valid data to the beginning of the input buffer.
                        Buffer.BlockCopy(_timeStretcherInputBuffer, _timeStretcherInputBufferReadOffset * sizeof(float),
                            _timeStretcherInputBuffer, 0, remaining * sizeof(float));
                        _timeStretcherInputBufferValidSamples = remaining;
                    }
                    else
                    {
                        // If no samples remain, the buffer is effectively empty.
                        _timeStretcherInputBufferValidSamples = 0;
                    }
                    // After compacting, the next read position is the start of the buffer.
                    _timeStretcherInputBufferReadOffset = 0;
                }

                // Read more data from the data provider into the time stretcher input buffer.
                var spaceToReadIntoInput = _timeStretcherInputBuffer.Length - _timeStretcherInputBufferValidSamples;
                if (spaceToReadIntoInput > 0)
                {
                    // Use a persistent read loop to fill the available space.
                    var targetSpan = _timeStretcherInputBuffer.AsSpan(_timeStretcherInputBufferValidSamples, spaceToReadIntoInput);
                    var totalReadFromProvider = 0;
                    while (!targetSpan.IsEmpty)
                    {
                        var readThisCall = _dataProvider.ReadBytes(targetSpan);
                        if (readThisCall == 0)
                        {
                            providerExhausted = true; // True end of stream signaled.
                            break;
                        }
                        totalReadFromProvider += readThisCall;
                        targetSpan = targetSpan.Slice(readThisCall);
                    }

                    _timeStretcherInputBufferValidSamples += totalReadFromProvider;
                    availableInStretcherInput = _timeStretcherInputBufferValidSamples - _timeStretcherInputBufferReadOffset;
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
            if (inputSpanForStretcher.IsEmpty && providerExhausted)
            {
                // If the input buffer for the stretcher is empty AND we know the provider is exhausted, flush the stretcher.
                samplesWrittenToResample = _timeStretcher.Flush(outputSpanForStretcher);
                samplesConsumedFromStretcherInputBuf = 0;
                sourceSamplesForThisProcessCall = 0;
            }
            else if (availableInStretcherInput >= _timeStretcher.MinInputSamplesToProcess)
            {
                // If there's enough input, process it.
                samplesWrittenToResample = _timeStretcher.Process(inputSpanForStretcher, outputSpanForStretcher,
                    out samplesConsumedFromStretcherInputBuf,
                    out sourceSamplesForThisProcessCall);
            }
            else
            {
                break; // Not enough input to process, and the provider is not yet exhausted.
            }

            // Update read offset and valid samples for time stretcher input buffer.
            if (samplesConsumedFromStretcherInputBuf > 0) _timeStretcherInputBufferReadOffset += samplesConsumedFromStretcherInputBuf;

            // Update resample buffer valid samples and total source samples advanced.
            _resampleBufferValidSamples += samplesWrittenToResample;
            totalSourceSamplesRepresented += sourceSamplesForThisProcessCall;

            // Break if no progress was made and no more data is expected.
            if (samplesWrittenToResample == 0 && samplesConsumedFromStretcherInputBuf == 0 && providerExhausted) break;
        }

        return totalSourceSamplesRepresented;
    }

    private void EnsureTimeStretcherBufferSize()
    {
        var minSize = _timeStretcher.MinInputSamplesToProcess;
        // Keep the logic similar to constructor: * 2 or min 8192*channels
        var targetSize = Math.Max(minSize * 2, 8192 * Format.Channels);
    
        if (_timeStretcherInputBuffer.Length < targetSize)
        {
            // Compact existing data to start before resizing
            if (_timeStretcherInputBufferValidSamples > 0)
            {
                if (_timeStretcherInputBufferReadOffset > 0)
                {
                    var remaining = _timeStretcherInputBufferValidSamples - _timeStretcherInputBufferReadOffset;
                    if (remaining > 0)
                    {
                        Buffer.BlockCopy(_timeStretcherInputBuffer, _timeStretcherInputBufferReadOffset * sizeof(float),
                            _timeStretcherInputBuffer, 0, remaining * sizeof(float));
                    }
                    _timeStretcherInputBufferValidSamples = remaining;
                    _timeStretcherInputBufferReadOffset = 0;
                }
            }
            else
            {
                _timeStretcherInputBufferValidSamples = 0;
                _timeStretcherInputBufferReadOffset = 0;
            }

            Array.Resize(ref _timeStretcherInputBuffer, targetSize);
        }
    }

    /// <summary>
    /// Handles the end-of-stream condition, including looping and stopping.
    /// This is called when the data provider is fully exhausted (ReadBytes returns 0).
    /// </summary>
    /// <param name="remainingOutputBuffer">The buffer for remaining output.</param>
    /// <param name="channels">The number of channels.</param>
    protected virtual void HandleEndOfStream(Span<float> remainingOutputBuffer, int channels)
    {
        // Not looping, and it's a file with a known length. This is the definitive end.
        if (!IsLooping && _dataProvider.Length > 0)
        {
            // Original end-of-stream handling
            if (!remainingOutputBuffer.IsEmpty)
            {
                var spaceToFill = remainingOutputBuffer.Length;
                var currentlyValidInResample = _resampleBufferValidSamples;

                if (currentlyValidInResample < spaceToFill)
                {
                    var sourceSamplesFromFinalFill = FillResampleBuffer(Math.Max(currentlyValidInResample, spaceToFill), channels);
                    _rawSamplePosition += sourceSamplesFromFinalFill;
                    _rawSamplePosition = Math.Min(_rawSamplePosition, _dataProvider.Length);
                }

                var toCopy = Math.Min(spaceToFill, _resampleBufferValidSamples);
                if (toCopy > 0)
                {
                    SafeCopyTo(_resampleBuffer.AsSpan(0, toCopy), remainingOutputBuffer.Slice(0, toCopy));
                    var remainingInResampleAfterCopy = _resampleBufferValidSamples - toCopy;
                    if (remainingInResampleAfterCopy > 0)
                    {
                        Buffer.BlockCopy(_resampleBuffer, toCopy * sizeof(float), _resampleBuffer, 0,
                            remainingInResampleAfterCopy * sizeof(float));
                    }

                    _resampleBufferValidSamples = remainingInResampleAfterCopy;
                    if (toCopy < spaceToFill)
                    {
                        remainingOutputBuffer.Slice(toCopy).Clear();
                    }
                }
                else
                {
                    remainingOutputBuffer.Clear();
                }
            }

            State = PlaybackState.Stopped;
            OnPlaybackEnded();
        }
        // Looping is enabled.
        else if (IsLooping)
        {
            var targetLoopStart = Math.Max(0, _loopStartSamples);
            var actualLoopEnd = (_loopEndSamples == -1)
                ? _dataProvider.Length
                : Math.Min(_loopEndSamples, _dataProvider.Length);

            // Check if the loop is valid (start < end, and start is within bounds).
            if (targetLoopStart < actualLoopEnd && targetLoopStart < _dataProvider.Length)
            {
                Seek(targetLoopStart, channels);
                if (!remainingOutputBuffer.IsEmpty)
                    GenerateAudio(remainingOutputBuffer, channels);
            }
            else
            {
                // Loop is not valid (e.g., start >= end), so treat as a normal end-of-stream.
                State = PlaybackState.Stopped;
                OnPlaybackEnded();
                remainingOutputBuffer.Clear();
            }
        }
        // For live streams (Length <= 0), just clear the buffer and continue
        else
        {
            remainingOutputBuffer.Clear();
        }
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
        Seek(0, Format.Channels);
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
        if (Format.Channels == 0 || Format.SampleRate == 0) return false;
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
        return Seek(targetTimeSeconds, Format.Channels);
    }

    /// <inheritdoc />
    public bool Seek(float timeInSeconds)
    {
        return Seek(timeInSeconds, Format.Channels);
    }
    
    private bool Seek(float timeInSeconds, int channels)
    {
        if (channels == 0 || Format.SampleRate == 0) return false;
        timeInSeconds = Math.Max(0, timeInSeconds);
        // Convert time in seconds to sample offset in source data.
        var sampleOffset = (int)(timeInSeconds / Duration * _dataProvider.Length);
        return Seek(sampleOffset, channels);
    }

    /// <inheritdoc />
    public bool Seek(int sampleOffset)
    {
        return Seek(sampleOffset, Format.Channels);
    }

    private bool Seek(int sampleOffset, int channels)
    {
        if (!_dataProvider.CanSeek || channels == 0) return false;

        var maxSeekableSample = _dataProvider.Length > 0 ? _dataProvider.Length - channels : 0;
        maxSeekableSample = Math.Max(0, maxSeekableSample);
        // Align sample offset to frame boundary.
        sampleOffset = (sampleOffset / channels) * channels;
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
    
    private static void SafeCopyTo(Span<float> source, Span<float> destination)
    {
        for (var i = 0; i < Math.Min(source.Length, destination.Length); i++)
        {
            destination[i] = Math.Clamp(source[i], -1f, 1f);
        }
    }

    #region Loop Point Configuration Methods

    /// <inheritdoc />
    public void SetLoopPoints(float startTime, float? endTime = null)
    {
        var channels = Format.Channels;
        var sampleRate = Format.SampleRate;
        if (channels == 0 || sampleRate == 0) return;

        if (startTime < 0)
            throw new ArgumentOutOfRangeException(nameof(startTime), "Loop start time cannot be negative.");

        var effectiveEndTime = endTime ?? -1f;
        if (Math.Abs(effectiveEndTime - -1f) > 1e-6f && effectiveEndTime < startTime)
            throw new ArgumentOutOfRangeException(nameof(endTime),
                "Loop end time must be greater than or equal to start time, or -1.");

        // Convert seconds to samples.
        _loopStartSamples = (int)(startTime * sampleRate * channels);
        _loopEndSamples = Math.Abs(effectiveEndTime - -1f) < 1e-6f
            ? -1
            : (int)(effectiveEndTime * sampleRate * channels);

        // Align to frame boundaries and clamp within data provider length.
        _loopStartSamples = (_loopStartSamples / channels) * channels;
        _loopStartSamples = Math.Clamp(_loopStartSamples, 0, _dataProvider.Length);

        if (_loopEndSamples != -1)
        {
            _loopEndSamples = _loopEndSamples / channels * channels;
            _loopEndSamples = Math.Clamp(_loopEndSamples, _loopStartSamples, _dataProvider.Length);
        }
    }

    /// <inheritdoc />
    public void SetLoopPoints(int startSample, int endSample = -1)
    {
        var channels = Format.Channels;
        if (channels == 0) return;

        if (startSample < 0)
            throw new ArgumentOutOfRangeException(nameof(startSample), "Loop start sample cannot be negative.");
        if (endSample != -1 && endSample < startSample)
            throw new ArgumentOutOfRangeException(nameof(endSample),
                "Loop end sample must be greater than or equal to start sample, or -1.");

        // Align to frame boundaries and clamp.
        _loopStartSamples = (startSample / channels) * channels;
        _loopStartSamples = Math.Clamp(_loopStartSamples, 0, _dataProvider.Length);

        if (endSample != -1)
        {
            endSample = Math.Max(startSample, endSample);
            _loopEndSamples = (endSample / channels) * channels;
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

    /// <inheritdoc cref="Dispose()" />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dataProvider.Dispose();
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (IsDisposed) return;
        Dispose(true);
        GC.SuppressFinalize(this);
        base.Dispose();
    }
}