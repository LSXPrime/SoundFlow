using System.Buffers;
using SoundFlow.Abstracts;
using SoundFlow.Interfaces;
using SoundFlow.Structs;

namespace SoundFlow.Editing;

/// <summary>
/// Represents a single audio segment (clip) placed on a timeline within an audio track.
/// It references a portion of an audio source and applies various playback settings and effects.
/// </summary>
public class AudioSegment : IDisposable
{
    private readonly bool _ownsDataProvider;
    private float[]? _reversedBufferCache;
    private int _reversedBufferCacheSourceLoopPass = -1;

    private string _name;
    private ISoundDataProvider _sourceDataProvider;
    private TimeSpan _sourceStartTime;
    private TimeSpan _sourceDuration;
    private TimeSpan _timelineStartTime;
    private AudioSegmentSettings _settings;
    private Track? _parentTrack;

    private WsolaTimeStretcher? _segmentWsolaStretcher;
    private float[] _wsolaFeedBuffer = [];
    private int _wsolaFeedBufferValidSamples;
    private int _wsolaFeedBufferReadOffset;

    private float[] _wsolaOutputBuffer = [];
    private int _wsolaOutputBufferValidSamples;
    private int _wsolaOutputBufferReadOffset;

    private long _currentSourceDataProviderPhysicalReadPos;
    private long _sourceSamplesFedToWsolaThisSourcePass;
    private int _currentSourcePassBeingFedToWsola;
    private long _currentStretchedPlayheadInSegmentLoopSamples;

    /// <summary>
    /// The audio format of the audio segment.
    /// </summary>
    public AudioFormat Format { get; }

    /// <summary>
    /// Gets or sets the name of the audio segment.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets the sound data provider that serves as the source for this audio segment.
    /// This property is set during construction and can be replaced via <see cref="ReplaceSource"/>.
    /// </summary>
    public ISoundDataProvider SourceDataProvider
    {
        get => _sourceDataProvider;
        private set => _sourceDataProvider = value;
    }

    /// <summary>
    /// Gets or sets the starting time offset within the <see cref="SourceDataProvider"/> from which this segment begins reading.
    /// </summary>
    public TimeSpan SourceStartTime
    {
        get => _sourceStartTime;
        set
        {
            if (_sourceStartTime == value) return;
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _sourceStartTime = value;
            FullResetState();
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets the duration of the audio to read from the <see cref="SourceDataProvider"/>, starting from <see cref="SourceStartTime"/>.
    /// This duration defines the base content of one playback instance before applying speed or loop settings.
    /// </summary>
    public TimeSpan SourceDuration
    {
        get => _sourceDuration;
        set
        {
            if (_sourceDuration == value) return;
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _sourceDuration = value;
            FullResetState();
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets the starting time of this segment on the overall composition timeline.
    /// </summary>
    public TimeSpan TimelineStartTime
    {
        get => _timelineStartTime;
        set
        {
            if (_timelineStartTime == value) return;
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _timelineStartTime = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets the playback and effect settings for this audio segment,
    /// such as volume, pan, fades, looping, and speed.
    /// </summary>
    public AudioSegmentSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value ?? throw new ArgumentNullException(nameof(value));
            _settings.ParentSegment = this;
            FullResetState();
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets the parent track to which this segment is added.
    /// </summary>
    internal Track? ParentTrack
    {
        get => _parentTrack;
        set => _parentTrack = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioSegment"/> class.
    /// </summary>
    /// <param name="format">The audio format of the audio segment. Cannot be null.</param>
    /// <param name="sourceDataProvider">The provider of the raw audio data for this segment. Cannot be null.</param>
    /// <param name="sourceStartTime">The starting time offset within the source data to begin reading from.</param>
    /// <param name="sourceDuration">The duration of the audio to read from the source data.</param>
    /// <param name="timelineStartTime">The starting time of this segment on the composition timeline.</param>
    /// <param name="name">Optional name for the segment. Defaults to "Segment".</param>
    /// <param name="settings">Optional audio segment settings. If null, default settings are used.</param>
    /// <param name="ownsDataProvider">
    /// A flag indicating whether this segment is responsible for disposing the <paramref name="sourceDataProvider"/>.
    /// Set to true if this segment is the primary owner of the provider; otherwise, false.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="sourceDataProvider"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="sourceStartTime"/> or <paramref name="timelineStartTime"/> is negative,
    /// or if <paramref name="sourceDuration"/> is zero or negative.
    /// </exception>
    public AudioSegment(
        AudioFormat format,
        ISoundDataProvider sourceDataProvider,
        TimeSpan sourceStartTime,
        TimeSpan sourceDuration,
        TimeSpan timelineStartTime,
        string name = "Segment",
        AudioSegmentSettings? settings = null,
        bool ownsDataProvider = false)
    {
        Format = format;
        _sourceDataProvider = sourceDataProvider ?? throw new ArgumentNullException(nameof(sourceDataProvider));
        _sourceStartTime = sourceStartTime;
        _sourceDuration = sourceDuration;
        _timelineStartTime = timelineStartTime;
        _name = name;
        _ownsDataProvider = ownsDataProvider;
        _settings = settings ?? new AudioSegmentSettings();
        _settings.ParentSegment = this;

        // Validation for initial construction
        if (_sourceStartTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(sourceStartTime));
        if (_sourceDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(sourceDuration));
        if (_timelineStartTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timelineStartTime));

        InitializeWsolaBuffers();
        FullResetState();
    }

    /// <summary>
    /// Initializes the internal buffers used for WSOLA time stretching.
    /// </summary>
    private void InitializeWsolaBuffers()
    {
        var channels = Format.Channels > 0 ? Format.Channels : 2;
        const int baseBufferSizeFrames = WsolaTimeStretcher.DefaultWindowSizeFrames * 8;
        _wsolaFeedBuffer = new float[baseBufferSizeFrames * channels];
        _wsolaOutputBuffer = new float[baseBufferSizeFrames * channels * 3];
    }


    /// <summary>
    /// Resets internal state variables used during ReadProcessedSamples.
    /// Crucially, this should be called if seeking occurs externally or if parameters change that affect reading.
    /// </summary>
    internal void FullResetState()
    {
        var channels = Format.Channels > 0 ? Format.Channels : 2;
        var sourceSampleRate = SourceDataProvider.SampleRate;

        // Reset data provider position to the beginning of the segment's source content.
        _currentSourceDataProviderPhysicalReadPos = (long)(SourceStartTime.TotalSeconds * sourceSampleRate * channels);
        if (SourceDataProvider.CanSeek) SourceDataProvider.Seek((int)_currentSourceDataProviderPhysicalReadPos);

        // Reset WSOLA-related counters and caches.
        _sourceSamplesFedToWsolaThisSourcePass = 0;
        _currentSourcePassBeingFedToWsola = 0;
        _currentStretchedPlayheadInSegmentLoopSamples = 0;

        _reversedBufferCache = null;
        _reversedBufferCacheSourceLoopPass = -1;

        // Clear WSOLA buffers state.
        _wsolaFeedBufferValidSamples = 0;
        _wsolaFeedBufferReadOffset = 0;
        _wsolaOutputBufferValidSamples = 0;
        _wsolaOutputBufferReadOffset = 0;

        // Initialize or reset WSOLA stretcher based on settings.
        var effectiveStretchFactor = Settings.TimeStretchFactor;
        if (Math.Abs(effectiveStretchFactor - 1.0f) > float.Epsilon && SourceDuration > TimeSpan.Zero)
        {
            // Create or reconfigure WSOLA if time stretching is enabled.
            _segmentWsolaStretcher ??= new WsolaTimeStretcher(channels, 1.0f / effectiveStretchFactor);
            _segmentWsolaStretcher.SetChannels(channels);
            _segmentWsolaStretcher.SetSpeed(1.0f / effectiveStretchFactor);
            _segmentWsolaStretcher.Reset();
        }
        else
        {
            _segmentWsolaStretcher = null;
        }
    }


    /// <summary>
    /// Resets the internal state for WSOLA when transitioning to a new source loop pass.
    /// This ensures WSOLA processes a fresh segment of the source audio.
    /// </summary>
    private void ResetForNewSourcePassForWsola()
    {
        var channels = Format.Channels > 0 ? Format.Channels : 2;
        var sourceSampleRate = SourceDataProvider.SampleRate;

        // Reset data provider position to the beginning of the source segment.
        _currentSourceDataProviderPhysicalReadPos = (long)(SourceStartTime.TotalSeconds * sourceSampleRate * channels);
        if (SourceDataProvider.CanSeek) SourceDataProvider.Seek((int)_currentSourceDataProviderPhysicalReadPos);

        // Reset counters for the new source pass.
        _sourceSamplesFedToWsolaThisSourcePass = 0;
        _reversedBufferCache = null;
        _reversedBufferCacheSourceLoopPass = -1;
        _segmentWsolaStretcher?.Reset();
        
        // Clear WSOLA internal buffers.
        _wsolaFeedBufferValidSamples = 0;
        _wsolaFeedBufferReadOffset = 0;
        _wsolaOutputBufferValidSamples = 0;
        _wsolaOutputBufferReadOffset = 0;
    }




    /// <summary>
    /// Gets the duration of the segment's core content after pitch-preserved time stretching has been applied.
    /// This does not yet account for SpeedFactor.
    /// </summary>
    public TimeSpan StretchedSourceDuration => SourceDuration <= TimeSpan.Zero
        ? TimeSpan.Zero
        : Settings.TargetStretchDuration ??
          TimeSpan.FromSeconds(SourceDuration.TotalSeconds * Settings.TimeStretchFactor);

    /// <summary>
    /// Gets the effective duration of a single instance of this segment on the timeline,
    /// considering the <see cref="AudioSegmentSettings.SpeedFactor"/>.
    /// Loop settings are not directly included here as they determine repetitions,
    /// not the base duration of one instance.
    /// </summary>
    public TimeSpan EffectiveDurationOnTimeline => Settings.SpeedFactor == 0
        ? TimeSpan.Zero
        : TimeSpan.FromTicks((long)(StretchedSourceDuration.Ticks / Settings.SpeedFactor));

    /// <summary>
    /// Gets the end time of this segment on the overall composition timeline,
    /// considering its start time and total looped duration.
    /// </summary>
    public TimeSpan TimelineEndTime => TimelineStartTime + GetTotalLoopedDurationOnTimeline();

    /// <summary>
    /// Calculates the total effective duration of this segment on the timeline,
    /// taking into account its <see cref="AudioSegmentSettings.Loop"/> settings
    /// (<see cref="LoopSettings.Repetitions"/> or <see cref="LoopSettings.TargetDuration"/>).
    /// </summary>
    /// <returns>A <see cref="TimeSpan"/> representing the total duration on the timeline.</returns>
    public TimeSpan GetTotalLoopedDurationOnTimeline()
    {
        var singleInstanceDuration = EffectiveDurationOnTimeline;
        if (singleInstanceDuration <= TimeSpan.Zero) return TimeSpan.Zero;
        return Settings.Loop.TargetDuration.HasValue
            ? Settings.Loop.TargetDuration.Value > TimeSpan.Zero // If TargetDuration is specified, use it (clamped to be positive), else use the single instance duration to effectively play once.
                ? Settings.Loop.TargetDuration.Value
                : singleInstanceDuration
            : TimeSpan.FromTicks(singleInstanceDuration.Ticks * (Settings.Loop.Repetitions + 1)); // Otherwise, use repetitions. Repetitions + 1 accounts for the initial play.
    }

    /// <summary>
    /// Creates a shallow clone of the current <see cref="AudioSegment"/> instance.
    /// The cloned segment shares the same <see cref="SourceDataProvider"/> but has its own independent settings.
    /// </summary>
    /// <param name="newTimelineStartTime">Optional new timeline start time for the cloned segment. If null, uses the original segment's timeline start time.</param>
    /// <returns>A new <see cref="AudioSegment"/> object with copied properties.</returns>
    public AudioSegment Clone(TimeSpan? newTimelineStartTime = null)
    {
        return new AudioSegment(Format, SourceDataProvider,
            SourceStartTime, SourceDuration, newTimelineStartTime ?? TimelineStartTime, $"{Name} (Clone)",
            Settings.Clone());
    }

    /// <summary>
    /// Replaces the underlying sound data source for this segment.
    /// If the original <see cref="SourceDataProvider"/> was owned by this segment, it will be disposed.
    /// </summary>
    /// <param name="newSource">The new sound data provider to use. Cannot be null.</param>
    /// <param name="newSourceStartTime">The new starting time offset within the new source data.</param>
    /// <param name="newSourceDuration">The new duration of the audio to read from the new source data.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="newSource"/> is null.</exception>
    internal void ReplaceSource(ISoundDataProvider newSource, TimeSpan newSourceStartTime, TimeSpan newSourceDuration)
    {
        // Dispose old owned provider if necessary
        if (_ownsDataProvider && !SourceDataProvider.IsDisposed)
            SourceDataProvider.Dispose();
        SourceDataProvider = newSource ?? throw new ArgumentNullException(nameof(newSource));
        SourceStartTime = newSourceStartTime;
        SourceDuration = newSourceDuration;
        FullResetState();
        MarkDirty();
    }

    /// <summary>
    /// Reads audio samples from this segment for a given time range within the segment.
    /// </summary>
    /// <param name="segmentTimelineOffset">The time offset *within this segment's effective timeline duration* to start reading from.</param>
    /// <param name="durationToRead">The duration of samples to read.</param>
    /// <param name="outputBuffer">The buffer to fill with samples.</param>
    /// <param name="outputBufferOffset">Offset in samples into the outputBuffer where writing should start.</param>
    /// <param name="targetSampleRate">The target sample rate for the output.</param>
    /// <param name="targetChannels">The target number of channels for the output.</param>
    /// <returns>Number of samples written to the output buffer.</returns>
    public int ReadProcessedSamples(TimeSpan segmentTimelineOffset, TimeSpan durationToRead, Span<float> outputBuffer,
        int outputBufferOffset, int targetSampleRate, int targetChannels)
    {
        // Early exit conditions for no processing or invalid input.
        if (!Settings.IsEnabled || durationToRead <= TimeSpan.Zero || outputBuffer.IsEmpty)
        {
            if (!outputBuffer.IsEmpty && outputBufferOffset < outputBuffer.Length)
                outputBuffer.Slice(outputBufferOffset).Clear();
            return 0;
        }

        var singleStretchedInstanceDuration = StretchedSourceDuration;
        if (singleStretchedInstanceDuration <= TimeSpan.Zero)
        {
            if (outputBufferOffset < outputBuffer.Length) outputBuffer.Slice(outputBufferOffset).Clear();
            return 0;
        }

        // Calculate current loop pass and time within that pass.
        var timeIntoTotalLoopedStretchedContent = segmentTimelineOffset;
        var currentSegmentLoopPass = singleStretchedInstanceDuration.Ticks > 0 ? (int)(timeIntoTotalLoopedStretchedContent.Ticks / singleStretchedInstanceDuration.Ticks) : 0;
        var timeOffsetWithinCurrentStretchedPass = TimeSpan.FromTicks(timeIntoTotalLoopedStretchedContent.Ticks % singleStretchedInstanceDuration.Ticks);

        if (!IsSegmentLoopEffectivelyInfinite(currentSegmentLoopPass) || Settings.Loop.TargetDuration.HasValue && segmentTimelineOffset >= Settings.Loop.TargetDuration.Value)
        {
            if (outputBufferOffset < outputBuffer.Length) outputBuffer.Slice(outputBufferOffset).Clear();
            return 0;
        }
        
        // Reset WSOLA state if transitioning to a new source loop pass.
        if (currentSegmentLoopPass != _currentSourcePassBeingFedToWsola && _segmentWsolaStretcher != null)
        {
            _currentSourcePassBeingFedToWsola = currentSegmentLoopPass;
            ResetForNewSourcePassForWsola();
            _currentStretchedPlayheadInSegmentLoopSamples = 0;
        }

        var sampleRateForStretchedOutput = SourceDataProvider.SampleRate;
        var stretchedDurationToFetch = TimeSpan.FromTicks((long)(durationToRead.Ticks * Settings.SpeedFactor));
        var samplesToFetchFromStretched = (int)(stretchedDurationToFetch.TotalSeconds * sampleRateForStretchedOutput * targetChannels);

        if (samplesToFetchFromStretched <= 0)
        {
            if (outputBufferOffset < outputBuffer.Length) outputBuffer.Slice(outputBufferOffset).Clear();
            return 0;
        }

        float[]? stretchedSamplesBuffer = null;
        float[]? resampledBuffer = null;
        float[]? tempProcessingFrame = null;

        try
        {
            stretchedSamplesBuffer = ArrayPool<float>.Shared.Rent(samplesToFetchFromStretched);
            var stretchedSamplesObtained = GetTimeStretchedSamples(
                timeOffsetWithinCurrentStretchedPass,
                stretchedDurationToFetch,
                stretchedSamplesBuffer.AsSpan(0, samplesToFetchFromStretched),
                sampleRateForStretchedOutput, targetChannels,
                currentSegmentLoopPass
            );

            if (stretchedSamplesObtained == 0)
            {
                if (outputBufferOffset < outputBuffer.Length) outputBuffer.Slice(outputBufferOffset).Clear();
                return 0;
            }

            var samplesToProcess = stretchedSamplesBuffer.AsSpan(0, stretchedSamplesObtained);

            // Calculate final output samples count, respecting output buffer size.
            var finalOutputSamplesCount = (int)(durationToRead.TotalSeconds * targetSampleRate * targetChannels);
            finalOutputSamplesCount = Math.Min(finalOutputSamplesCount, outputBuffer.Length - outputBufferOffset);

            // Apply SpeedFactor (resampling) if needed
            if (Math.Abs(Settings.SpeedFactor - 1.0f) > float.Epsilon || sampleRateForStretchedOutput != targetSampleRate)
            {
                if (finalOutputSamplesCount > 0)
                {
                    resampledBuffer = ArrayPool<float>.Shared.Rent(finalOutputSamplesCount);
                    SimpleResample(samplesToProcess, resampledBuffer.AsSpan(0, finalOutputSamplesCount), targetChannels);
                    samplesToProcess = resampledBuffer.AsSpan(0, finalOutputSamplesCount);
                }
                else
                {
                    samplesToProcess = Span<float>.Empty;
                }
            }
            else if (samplesToProcess.Length > finalOutputSamplesCount)
            {
                // If no resampling but more samples obtained than needed, truncate.
                samplesToProcess = samplesToProcess.Slice(0, finalOutputSamplesCount);
            }

            if (samplesToProcess.IsEmpty)
            {
                if (outputBufferOffset < outputBuffer.Length) outputBuffer.Slice(outputBufferOffset).Clear();
                return 0;
            }

            // Apply Modifiers, Fades, Volume, Pan frame by frame
            tempProcessingFrame = ArrayPool<float>.Shared.Rent(targetChannels);
            var processingFrameSpan = tempProcessingFrame.AsSpan(0, targetChannels);
            var samplesWrittenToOutput = 0;
            var singleEffectiveInstanceDurationOnTimeline = EffectiveDurationOnTimeline;

            for (var frameIndex = 0; frameIndex < samplesToProcess.Length / targetChannels; frameIndex++)
            {
                // Ensure there's enough space in the output buffer for the current frame.
                if (outputBufferOffset + samplesWrittenToOutput + targetChannels > outputBuffer.Length) break;
                samplesToProcess.Slice(frameIndex * targetChannels, targetChannels).CopyTo(processingFrameSpan);

                // Apply Modifiers
                foreach (var modifier in Settings.Modifiers)
                    modifier.Process(processingFrameSpan, targetChannels);
                
                // Apply Analyzers
                foreach (var analyzer in Settings.Analyzers)
                    analyzer.Process(processingFrameSpan, targetChannels);

                // Calculate current time within the effective timeline instance for fade and other time-dependent effects.
                var timeIntoCurrentEffectiveLoopInstance = MapStretchedOffsetToEffectiveTimelineOffset(timeOffsetWithinCurrentStretchedPass, Settings.SpeedFactor) 
                                                           + TimeSpan.FromSeconds((double)frameIndex / targetSampleRate);

                for (var ch = 0; ch < targetChannels; ch++)
                {
                    var sample = processingFrameSpan[ch];
                    var fadeMultiplier = 1.0f;
                    
                    // Apply Fade In
                    if (Settings.FadeInDuration > TimeSpan.Zero && timeIntoCurrentEffectiveLoopInstance < Settings.FadeInDuration)
                        fadeMultiplier *= GetFadeMultiplier(timeIntoCurrentEffectiveLoopInstance.TotalSeconds / Settings.FadeInDuration.TotalSeconds, Settings.FadeInCurve, false);
                    
                    // Apply Fade Out
                    if (Settings.FadeOutDuration > TimeSpan.Zero && timeIntoCurrentEffectiveLoopInstance > singleEffectiveInstanceDurationOnTimeline - Settings.FadeOutDuration)
                    {
                        var timeIntoFadeOut = (timeIntoCurrentEffectiveLoopInstance - (singleEffectiveInstanceDurationOnTimeline - Settings.FadeOutDuration)).TotalSeconds;
                        var fadeOutProgress = Math.Clamp(timeIntoFadeOut / Settings.FadeOutDuration.TotalSeconds, 0.0, 1.0);
                        fadeMultiplier *= GetFadeMultiplier(fadeOutProgress, Settings.FadeOutCurve, true);
                    }

                    sample *= fadeMultiplier * Settings.Volume;  // 0=L, 0.5=C, 1=R
                    if (targetChannels == 2)
                    {
                        var panFactor = (Settings.Pan + 1.0f) / 2.0f;
                        sample *= ch == 0
                            ? MathF.Sqrt(1.0f - panFactor) * 1.41421356f // L
                            : MathF.Sqrt(panFactor) * 1.41421356f; // R (Equal power approx)
                    }

                    outputBuffer[outputBufferOffset + samplesWrittenToOutput++] = Math.Clamp(sample, -1f, 1f);
                }
            }

            // Clear any remaining portion of the output buffer if not fully filled.
            if (outputBufferOffset + samplesWrittenToOutput < outputBuffer.Length && samplesWrittenToOutput < finalOutputSamplesCount)
            {
                outputBuffer.Slice(outputBufferOffset + samplesWrittenToOutput).Clear();
            }

            return samplesWrittenToOutput;
        }
        finally
        {
            // Return rented buffers to the pool.
            if (stretchedSamplesBuffer != null) ArrayPool<float>.Shared.Return(stretchedSamplesBuffer, true);
            if (resampledBuffer != null) ArrayPool<float>.Shared.Return(resampledBuffer, true);
            if (tempProcessingFrame != null) ArrayPool<float>.Shared.Return(tempProcessingFrame, true);
        }
    }


    /// <summary>
    /// Gets samples that are either directly from the source (if no stretch) or
    /// processed through WSOLA for pitch-preserved time stretching.
    /// Handles looping of the source material before it's fed to WSOLA or returned.
    /// </summary>
    /// <param name="timeOffsetInCurrentStretchedPass">The time offset within the current stretched loop pass to start reading.</param>
    /// <param name="stretchedDurationToRead">The duration of stretched audio to read.</param>
    /// <param name="outputBuffer">The buffer to fill with stretched samples.</param>
    /// <param name="sampleRate">The sample rate of the stretched audio (same as source for WSOLA).</param>
    /// <param name="channels">The number of channels.</param>
    /// <param name="currentSegmentLoopPassContext">The current loop pass index for context, especially for non-WSOLA path.</param>
    /// <returns>The number of samples written to the output buffer.</returns>
    private int GetTimeStretchedSamples(TimeSpan timeOffsetInCurrentStretchedPass, TimeSpan stretchedDurationToRead, Span<float> outputBuffer,
        int sampleRate, int channels, int currentSegmentLoopPassContext
    )
    {
        // If no WSOLA stretching, just read raw (handling loops and reversal internally)
        if (_segmentWsolaStretcher == null || Math.Abs(Settings.TimeStretchFactor - 1.0f) < float.Epsilon)
        {
            return ReadAndReverseSourceSamples(timeOffsetInCurrentStretchedPass, stretchedDurationToRead, outputBuffer,
                sampleRate, channels, currentSegmentLoopPassContext, isFeedingWsola: false);
        }

        // WSOLA Path
        var samplesNeededForRequest = (int)(stretchedDurationToRead.TotalSeconds * sampleRate * channels);
        samplesNeededForRequest = Math.Min(samplesNeededForRequest, outputBuffer.Length);
        if (samplesNeededForRequest <= 0) return 0;

        var samplesFilledForRequest = 0;
        
        // Calculate target start sample within the stretched segment loop.
        var targetStartSampleInStretchedStreamThisSegmentLoop = (long)(timeOffsetInCurrentStretchedPass.TotalSeconds * sampleRate * channels);

        // Determine how many samples need to be skipped from the WSOLA output buffer to reach the target start.
        var samplesToSkipFromWsolaOutputBuffer = targetStartSampleInStretchedStreamThisSegmentLoop - _currentStretchedPlayheadInSegmentLoopSamples;

        // Skip samples that are already available in the WSOLA output buffer.
        var canSkipFromCurrentBuffer = Math.Min(samplesToSkipFromWsolaOutputBuffer, _wsolaOutputBufferValidSamples - _wsolaOutputBufferReadOffset);
        if (canSkipFromCurrentBuffer > 0)
        {
            _wsolaOutputBufferReadOffset += (int)canSkipFromCurrentBuffer;
            _currentStretchedPlayheadInSegmentLoopSamples += canSkipFromCurrentBuffer;
            samplesToSkipFromWsolaOutputBuffer -= canSkipFromCurrentBuffer;
        }

        while (samplesFilledForRequest < samplesNeededForRequest)
        {
            // Continue skipping if more samples need to be generated and skipped.
            while (samplesToSkipFromWsolaOutputBuffer > 0)
            {
                CompactWsolaOutputBuffer();
                if (!EnsureMoreWsolaOutputGenerated(sampleRate, channels))
                {
                    if (samplesFilledForRequest < outputBuffer.Length)
                        outputBuffer.Slice(samplesFilledForRequest).Clear();
                    
                    return samplesFilledForRequest;
                }

                // Try skipping again with newly generated output.
                canSkipFromCurrentBuffer = Math.Min(samplesToSkipFromWsolaOutputBuffer, _wsolaOutputBufferValidSamples - _wsolaOutputBufferReadOffset);
                _wsolaOutputBufferReadOffset += (int)canSkipFromCurrentBuffer;
                _currentStretchedPlayheadInSegmentLoopSamples += canSkipFromCurrentBuffer;
                samplesToSkipFromWsolaOutputBuffer -= canSkipFromCurrentBuffer;

                if (canSkipFromCurrentBuffer == 0 && samplesToSkipFromWsolaOutputBuffer > 0)
                {
                    // If no samples were skipped despite still needing to skip, means no more data.
                    if (samplesFilledForRequest < outputBuffer.Length)
                        outputBuffer.Slice(samplesFilledForRequest).Clear();
                    
                    return samplesFilledForRequest;
                }
            }

            // 1. Consume from existing output buffer
            var availableInWsolaOutput = _wsolaOutputBufferValidSamples - _wsolaOutputBufferReadOffset;
            var toCopyNow = Math.Min(samplesNeededForRequest - samplesFilledForRequest, availableInWsolaOutput);

            if (toCopyNow > 0)
            {
                // Copy available WSOLA output to the request buffer.
                _wsolaOutputBuffer.AsSpan(_wsolaOutputBufferReadOffset, toCopyNow).CopyTo(outputBuffer.Slice(samplesFilledForRequest));
                samplesFilledForRequest += toCopyNow;
                _wsolaOutputBufferReadOffset += toCopyNow;
                _currentStretchedPlayheadInSegmentLoopSamples += toCopyNow;
            }

            // If more samples are still needed, try generating more WSOLA output.
            if (samplesFilledForRequest < samplesNeededForRequest)
            {
                CompactWsolaOutputBuffer();
                if (!EnsureMoreWsolaOutputGenerated(sampleRate, channels))
                {
                    if (samplesFilledForRequest < outputBuffer.Length)
                        outputBuffer.Slice(samplesFilledForRequest).Clear();
                    
                    break;
                }
            }
        }

        return samplesFilledForRequest;
    }

    /// <summary>
    /// Compacts the WSOLA output buffer by shifting valid data to the beginning
    /// and resetting the read offset.
    /// </summary>
    private void CompactWsolaOutputBuffer()
    {
        if (_wsolaOutputBufferReadOffset <= 0) return;
        if (_wsolaOutputBufferValidSamples > _wsolaOutputBufferReadOffset)
        {
            // Shift unread valid data to the start of the buffer.
            Buffer.BlockCopy(_wsolaOutputBuffer, _wsolaOutputBufferReadOffset * sizeof(float),
                _wsolaOutputBuffer, 0,
                (_wsolaOutputBufferValidSamples - _wsolaOutputBufferReadOffset) * sizeof(float));
        }

        _wsolaOutputBufferValidSamples -= _wsolaOutputBufferReadOffset;
        _wsolaOutputBufferReadOffset = 0;
    }


    /// <summary>
    /// Ensures that the WSOLA output buffer has enough data by feeding more source samples
    /// into the WSOLA algorithm and processing them.
    /// Handles source looping for continuous feed to WSOLA.
    /// </summary>
    /// <param name="sampleRate">The sample rate of the source audio.</param>
    /// <param name="channels">The number of channels.</param>
    /// <returns>True if more output was generated, false otherwise (e.g., end of segment).</returns>
    private bool EnsureMoreWsolaOutputGenerated(int sampleRate, int channels)
    {
        if (_segmentWsolaStretcher == null) return false;

        var sourceSamplesInOneSourcePass = (long)(SourceDuration.TotalSeconds * sampleRate * channels);
        if (sourceSamplesInOneSourcePass <= 0) return false;
        
        var initialWsolaOutputCountThisCall = _wsolaOutputBufferValidSamples;
        
        // Loop until enough output is generated or source runs out.
        while (_wsolaOutputBufferValidSamples - _wsolaOutputBufferReadOffset < WsolaTimeStretcher.DefaultWindowSizeFrames * channels)
        {
            var currentSourcePassExhaustedForWsolaFeed = _sourceSamplesFedToWsolaThisSourcePass >= sourceSamplesInOneSourcePass;

            // Handle source looping: if current pass is exhausted, reset for the next pass.
            if (currentSourcePassExhaustedForWsolaFeed)
            {
                if (IsSegmentLoopEffectivelyInfinite(_currentSourcePassBeingFedToWsola))
                {
                    _currentSourcePassBeingFedToWsola++;
                    ResetForNewSourcePassForWsola();
                }
                else
                {
                    break;
                }
            }

            CompactWsolaFeedBuffer();
            
            // If feed buffer needs more data.
            if (_wsolaFeedBufferValidSamples < _segmentWsolaStretcher.MinInputSamplesToProcess)
            {
                var spaceToFillInFeed = _wsolaFeedBuffer.Length - _wsolaFeedBufferValidSamples;
                if (spaceToFillInFeed > 0)
                {
                    // Calculate how many samples are left in the current source pass.
                    var remainingInSourcePassForFeed = sourceSamplesInOneSourcePass - _sourceSamplesFedToWsolaThisSourcePass;
                    var samplesToReadForFeed = Math.Min(spaceToFillInFeed, (int)remainingInSourcePassForFeed);
                    var sourceDurationToReadForFeed = TimeSpan.FromSeconds(samplesToReadForFeed / (double)(sampleRate * channels));
                    var timeOffsetInCurrentSourcePassForFeed = TimeSpan.FromSeconds(_sourceSamplesFedToWsolaThisSourcePass / (double)(sampleRate * channels));

                    if (sourceDurationToReadForFeed > TimeSpan.Zero)
                    {
                        // Read raw source samples into the feed buffer.
                        var rawSamplesRead = ReadAndReverseSourceSamples(
                            timeOffsetInCurrentSourcePassForFeed,
                            sourceDurationToReadForFeed,
                            _wsolaFeedBuffer.AsSpan(_wsolaFeedBufferValidSamples, spaceToFillInFeed),
                            sampleRate, channels,
                            _currentSourcePassBeingFedToWsola,
                            isFeedingWsola: true);

                        _wsolaFeedBufferValidSamples += rawSamplesRead;
                    }
                }
            }

            // Prepare input and output spans for WSOLA processing.
            var inputForWsolaSpan = _wsolaFeedBuffer.AsSpan(_wsolaFeedBufferReadOffset, _wsolaFeedBufferValidSamples - _wsolaFeedBufferReadOffset);
            var outputSpaceForWsolaSpan = _wsolaOutputBuffer.AsSpan(_wsolaOutputBufferValidSamples);
            int samplesWrittenToWsolaOut, samplesConsumedFromFeed, sourceSamplesRepresentedThisWsolaCall;
            var endOfCurrentSourcePassFeed = _sourceSamplesFedToWsolaThisSourcePass + inputForWsolaSpan.Length >= sourceSamplesInOneSourcePass;
            if (inputForWsolaSpan.IsEmpty && (!IsSegmentLoopEffectivelyInfinite(_currentSourcePassBeingFedToWsola) || endOfCurrentSourcePassFeed)) break;

            // Handle flushing WSOLA if nearing the end of the source and not looping infinitely.
            if (endOfCurrentSourcePassFeed && 
                inputForWsolaSpan.Length < _segmentWsolaStretcher.MinInputSamplesToProcess &&
                !IsSegmentLoopEffectivelyInfinite(_currentSourcePassBeingFedToWsola))
            {
                samplesWrittenToWsolaOut = _segmentWsolaStretcher.Flush(outputSpaceForWsolaSpan);
                samplesConsumedFromFeed = inputForWsolaSpan.Length;
                sourceSamplesRepresentedThisWsolaCall = 0;
            }
            // If not enough input for normal process, and not flushing, break.
            else if (inputForWsolaSpan.Length < _segmentWsolaStretcher.MinInputSamplesToProcess)
            {
                break;
            }
            else
            {
                // Normal WSOLA processing.
                samplesWrittenToWsolaOut = _segmentWsolaStretcher.Process(inputForWsolaSpan, outputSpaceForWsolaSpan,
                    out samplesConsumedFromFeed, out sourceSamplesRepresentedThisWsolaCall);
            }

            if (samplesConsumedFromFeed > 0)
            {
                _wsolaFeedBufferReadOffset += samplesConsumedFromFeed;
                _sourceSamplesFedToWsolaThisSourcePass += sourceSamplesRepresentedThisWsolaCall;
            }

            if (samplesWrittenToWsolaOut > 0) _wsolaOutputBufferValidSamples += samplesWrittenToWsolaOut;

            // Break if no progress is made or enough output is generated.
            if (samplesWrittenToWsolaOut == 0 && samplesConsumedFromFeed == 0) break;
            if (_wsolaOutputBufferValidSamples - _wsolaOutputBufferReadOffset >= WsolaTimeStretcher.DefaultWindowSizeFrames * channels) break;
        }

        // Return true if the output buffer grew during this call.
        return _wsolaOutputBufferValidSamples > initialWsolaOutputCountThisCall;
    }

    /// <summary>
    /// Compacts the WSOLA feed buffer by shifting valid data to the beginning
    /// and resetting the read offset.
    /// </summary>
    private void CompactWsolaFeedBuffer()
    {
        if (_wsolaFeedBufferReadOffset <= 0) return;
        if (_wsolaFeedBufferValidSamples > _wsolaFeedBufferReadOffset)
        {
            // Shift unread valid data to the start of the buffer.
            Buffer.BlockCopy(_wsolaFeedBuffer, _wsolaFeedBufferReadOffset * sizeof(float),
                _wsolaFeedBuffer, 0,
                (_wsolaFeedBufferValidSamples - _wsolaFeedBufferReadOffset) * sizeof(float));
        }

        _wsolaFeedBufferValidSamples -= _wsolaFeedBufferReadOffset;
        _wsolaFeedBufferReadOffset = 0;
    }

    /// <summary>
    /// Reads and reverses source samples from the data provider starting from given time offset
    /// and writes them to the given output buffer.
    /// </summary>
    /// <param name="timeOffsetInCurrentSourcePass">Time offset in the current source pass.</param>
    /// <param name="durationToRead">Duration to read from the source pass.</param>
    /// <param name="outputBuffer">The output buffer to write to.</param>
    /// <param name="sampleRate">Sample rate of the source audio.</param>
    /// <param name="channels">Number of channels in the source audio.</param>
    /// <param name="currentSegmentLoopPassContext">Current pass number of the source loop. Used for caching reversed data.</param>
    /// <param name="isFeedingWsola">Whether the read samples are being fed to the WSOLA time stretcher. Affects loop pass tracking.</param>
    /// <returns>The number of samples written to the output buffer.</returns>
    private int ReadAndReverseSourceSamples(TimeSpan timeOffsetInCurrentSourcePass, TimeSpan durationToRead, Span<float> outputBuffer, 
        int sampleRate, int channels, int currentSegmentLoopPassContext, bool isFeedingWsola = false)
    {
        var samplesToReadTotal = (int)(durationToRead.TotalSeconds * sampleRate * channels);
        samplesToReadTotal = Math.Min(samplesToReadTotal, outputBuffer.Length);
        if (samplesToReadTotal <= 0) return 0;

        var samplesWrittenToOutput = 0;
        var singlePassSourceSamples = (long)(SourceDuration.TotalSeconds * sampleRate * channels);
        if (singlePassSourceSamples <= 0) return 0;

        // Calculate the current sample offset within the source pass for the current read request.
        var currentEffectiveSampleOffsetInSourcePass = (long)(timeOffsetInCurrentSourcePass.TotalSeconds * sampleRate * channels);

        if (Settings.IsReversed)
        {
            // Ensure reversed buffer for the current loop iteration is populated
            var cacheKeySourceLoopPass = isFeedingWsola ? _currentSourcePassBeingFedToWsola : currentSegmentLoopPassContext;
            if (_reversedBufferCache == null || _reversedBufferCacheSourceLoopPass != cacheKeySourceLoopPass)
            {
                _reversedBufferCache = ArrayPool<float>.Shared.Rent((int)singlePassSourceSamples);
                var physicalReadStart = (long)(SourceStartTime.TotalSeconds * sampleRate * channels);
                if (SourceDataProvider.CanSeek && SourceDataProvider.Position != physicalReadStart)
                    SourceDataProvider.Seek((int)physicalReadStart);

                var cachedSamples = 0;
                var tempChunk = ArrayPool<float>.Shared.Rent(WsolaTimeStretcher.DefaultWindowSizeFrames * channels * 2);
                try
                {
                    // Read the entire source segment into the cache.
                    while (cachedSamples < singlePassSourceSamples)
                    {
                        var toReadNow = Math.Min((int)singlePassSourceSamples - cachedSamples, tempChunk.Length);
                        var r = SourceDataProvider.ReadBytes(tempChunk.AsSpan(0, toReadNow));
                        if (r == 0) break;
                        tempChunk.AsSpan(0, r).CopyTo(_reversedBufferCache.AsSpan(cachedSamples));
                        cachedSamples += r;
                    }
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(tempChunk, true);
                }

                if (cachedSamples < singlePassSourceSamples) Array.Resize(ref _reversedBufferCache, cachedSamples);
                singlePassSourceSamples = cachedSamples;

                ReverseBufferInterleaved(_reversedBufferCache.AsSpan(0, (int)singlePassSourceSamples), channels);
                _reversedBufferCacheSourceLoopPass = cacheKeySourceLoopPass;
                _currentSourceDataProviderPhysicalReadPos = physicalReadStart + singlePassSourceSamples;
            }
            
            // Read from the reversed segment content buffer
            var reversedReadStartSampleInCache = currentEffectiveSampleOffsetInSourcePass;
            var samplesAvailableInReversedCache = singlePassSourceSamples - reversedReadStartSampleInCache;
            var toCopyFromReversed = Math.Min(samplesToReadTotal, (int)samplesAvailableInReversedCache);
            toCopyFromReversed = Math.Max(0, toCopyFromReversed);

            if (_reversedBufferCache != null && toCopyFromReversed > 0 && reversedReadStartSampleInCache < _reversedBufferCache.Length)
            {
                _reversedBufferCache.AsSpan((int)reversedReadStartSampleInCache, toCopyFromReversed)
                    .CopyTo(outputBuffer.Slice(0, toCopyFromReversed));
                samplesWrittenToOutput = toCopyFromReversed;
            }

            // Clear any remaining portion of the output buffer if not fully filled.
            if (samplesWrittenToOutput < outputBuffer.Length && samplesWrittenToOutput < samplesToReadTotal)
                outputBuffer.Slice(samplesWrittenToOutput).Clear();

            return samplesWrittenToOutput;
        }

        // Normal (non-reversed) read
        while (samplesWrittenToOutput < samplesToReadTotal)
        {
            var physicalProviderTargetReadPos = _currentSourceDataProviderPhysicalReadPos;
            if (SourceDataProvider.CanSeek && SourceDataProvider.Position != physicalProviderTargetReadPos) 
                SourceDataProvider.Seek((int)physicalProviderTargetReadPos);


            // Calculate samples left in the current physical source pass.
            var samplesLeftInThisSourcePass = singlePassSourceSamples - (physicalProviderTargetReadPos - (long)(SourceStartTime.TotalSeconds * sampleRate * channels));
            var samplesToReadThisIteration = Math.Min(samplesToReadTotal - samplesWrittenToOutput, (int)samplesLeftInThisSourcePass);
            samplesToReadThisIteration = Math.Max(0, samplesToReadThisIteration);


            if (samplesToReadThisIteration <= 0)
            {
                // If current pass is exhausted, handle looping.
                if (IsSegmentLoopEffectivelyInfinite(isFeedingWsola ? _currentSourcePassBeingFedToWsola : currentSegmentLoopPassContext))
                {
                    _currentSourceDataProviderPhysicalReadPos = (long)(SourceStartTime.TotalSeconds * sampleRate * channels);
                    if (isFeedingWsola) _currentSourcePassBeingFedToWsola++;
                    continue;
                }

                break;
            }

            // Read samples from the source data provider.
            var readCount = SourceDataProvider.ReadBytes(outputBuffer.Slice(samplesWrittenToOutput, samplesToReadThisIteration));

            if (readCount == 0)
            {
                if (IsSegmentLoopEffectivelyInfinite(isFeedingWsola ? _currentSourcePassBeingFedToWsola : currentSegmentLoopPassContext))
                {
                    _currentSourceDataProviderPhysicalReadPos = (long)(SourceStartTime.TotalSeconds * sampleRate * channels);
                    if (SourceDataProvider.CanSeek) SourceDataProvider.Seek((int)_currentSourceDataProviderPhysicalReadPos);
                    if (isFeedingWsola) _currentSourcePassBeingFedToWsola++;
                    continue;
                }

                if (samplesWrittenToOutput < outputBuffer.Length && samplesWrittenToOutput < samplesToReadTotal)
                    outputBuffer.Slice(samplesWrittenToOutput).Clear();
                break;
            }

            _currentSourceDataProviderPhysicalReadPos += readCount;
            samplesWrittenToOutput += readCount;
        }

        return samplesWrittenToOutput;
    }

    /// <summary>
    /// Determines if the segment is configured to loop indefinitely or for a specific number of repetitions/duration.
    /// </summary>
    /// <param name="currentSegmentLoopPassNumberBeingProcessed">The current loop iteration count (0-indexed).</param>
    /// <returns>True if the segment should continue looping for the given pass, false otherwise.</returns>
    private bool IsSegmentLoopEffectivelyInfinite(int currentSegmentLoopPassNumberBeingProcessed)
    {
        if (!Settings.Loop.TargetDuration.HasValue)
            return currentSegmentLoopPassNumberBeingProcessed <= Settings.Loop.Repetitions;
        var singleStretchedInstanceDuration = StretchedSourceDuration;
        if (singleStretchedInstanceDuration <= TimeSpan.Zero) return false;
        var maxLoopsByDuration = (int)Math.Floor(Settings.Loop.TargetDuration.Value.TotalSeconds / singleStretchedInstanceDuration.TotalSeconds);
        return currentSegmentLoopPassNumberBeingProcessed < maxLoopsByDuration;

    }

    /// <summary>
    /// Maps a time offset within the stretched content to its corresponding time offset
    /// within the effective timeline instance by applying the speed factor.
    /// </summary>
    /// <param name="stretchedOffset">The time offset within the stretched content.</param>
    /// <param name="speedFactor">The speed factor applied to the segment.</param>
    /// <returns>The corresponding time offset on the effective timeline.</returns>
    private TimeSpan MapStretchedOffsetToEffectiveTimelineOffset(TimeSpan stretchedOffset, float speedFactor)
    {
        return speedFactor == 0 ? TimeSpan.MaxValue : TimeSpan.FromTicks((long)(stretchedOffset.Ticks / speedFactor));
    }

    /// <summary>
    /// Reverses the order of frames in an interleaved audio buffer.
    /// </summary>
    /// <param name="buffer">The buffer containing interleaved audio samples (e.g., LRLR...).</param>
    /// <param name="channels">The number of channels in the audio.</param>
    private void ReverseBufferInterleaved(Span<float> buffer, int channels)
    {
        if (channels == 0 || buffer.Length == 0 || buffer.Length % channels != 0) return;
        var frameCount = buffer.Length / channels;
        var tempFrame = ArrayPool<float>.Shared.Rent(channels);
        try
        {
            for (var i = 0; i < frameCount / 2; i++)
            {
                var frameStartIndex = i * channels;
                var frameEndIndex = (frameCount - 1 - i) * channels;

                // Swap frame i with frame (frameCount - 1 - i)
                buffer.Slice(frameStartIndex, channels).CopyTo(tempFrame.AsSpan(0, channels));
                buffer.Slice(frameEndIndex, channels).CopyTo(buffer.Slice(frameStartIndex, channels));
                tempFrame.AsSpan(0, channels).CopyTo(buffer.Slice(frameEndIndex, channels));
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(tempFrame, true);
        }
    }

    /// <summary>
    /// Performs simple linear interpolation resampling of audio samples.
    /// </summary>
    /// <param name="input">The input buffer with samples to resample.</param>
    /// <param name="output">The output buffer to write resampled samples to.</param>
    /// <param name="channels">The number of channels.</param>
    private static void SimpleResample(ReadOnlySpan<float> input, Span<float> output, int channels)
    {
        if (input.IsEmpty || output.IsEmpty || channels == 0)
        {
            if (!output.IsEmpty) output.Clear();
            return;
        }

        var inputFrames = input.Length / channels;
        var outputFrames = output.Length / channels;
        if (inputFrames == 0 || outputFrames == 0)
        {
            if (!output.IsEmpty) output.Clear();
            return;
        }

        // Calculate the ratio of input frames to output frames.
        var ratio = (outputFrames == 1 || inputFrames == 1) ? 0 : (double)(inputFrames - 1) / (outputFrames - 1);
        for (var i = 0; i < outputFrames; i++)
        {
            var inputFrameDouble = i * ratio;
            var inputFrameFloor = (int)inputFrameDouble;
            var fraction = inputFrameDouble - inputFrameFloor;
            inputFrameFloor = Math.Clamp(inputFrameFloor, 0, inputFrames - 1);
            var inputFrameCeil = Math.Min(inputFrameFloor + 1, inputFrames - 1);
            for (var ch = 0; ch < channels; ch++)
            {
                // Linear interpolation
                var s0 = input[inputFrameFloor * channels + ch];
                var s1 = input[inputFrameCeil * channels + ch];
                output[i * channels + ch] = s0 + (float)(fraction * (s1 - s0));
            }
        }
    }

    /// <summary>
    /// Calculates a fade multiplier based on progress through the fade duration and the specified curve type.
    /// </summary>
    /// <param name="progress">The normalized progress through the fade, from 0.0 to 1.0.</param>
    /// <param name="curve">The type of fade curve to apply (Linear, Logarithmic, S-Curve).</param>
    /// <param name="isFadingOutEffect">If true, the progress is inverted (1.0 - progress) to correctly apply fade-out curves.</param>
    /// <returns>A float multiplier (0.0 to 1.0) to apply to the audio sample.</returns>
    private static float GetFadeMultiplier(double progress, FadeCurveType curve, bool isFadingOutEffect)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);
        var rawMultiplier = curve switch
        {
            FadeCurveType.Linear => (float)progress,
            FadeCurveType.Logarithmic => (float)Math.Pow(progress, 2), // Approximates a logarithmic curve
            FadeCurveType.SCurve => (float)(progress * progress * (3.0 - 2.0 * progress)), // Smoothstep function
            _ => (float)progress, // Default to linear if curve type is unknown
        };
        // For fade-out, the multiplier should go from 1 to 0 as progress goes from 0 to 1.
        return isFadingOutEffect ? (1.0f - rawMultiplier) : rawMultiplier;
    }

    /// <summary>
    /// Disposes of the <see cref="SourceDataProvider"/> if this segment is marked as owning it.
    /// This frees up resources held by the audio data provider.
    /// </summary>
    public void Dispose()
    {
        if (_ownsDataProvider && !SourceDataProvider.IsDisposed)
            SourceDataProvider.Dispose();
        
        _segmentWsolaStretcher = null;
        if (_reversedBufferCache != null)
        {
            ArrayPool<float>.Shared.Return(_reversedBufferCache, clearArray: true);
            _reversedBufferCache = null;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Marks the parent track (and transitively the composition) as dirty (having unsaved changes).
    /// This should be called by any method that modifies the segment's state.
    /// </summary>
    public void MarkDirty()
    {
        ParentTrack?.MarkDirty();
    }
}
