using System.Buffers;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;

namespace SoundFlow.Components;

/// <summary>
/// A sound player that plays audio from a data provider.
/// </summary>
public sealed class SoundPlayer(ISoundDataProvider dataProvider) : SoundComponent, ISoundPlayer
{
    private readonly ISoundDataProvider _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
    private int _samplePosition;
    private float _currentFrame;
    private float _playbackSpeed = 1.0f;

    private int _loopStartSamples;
    private int _loopEndSamples = -1;

    /// <summary>
    /// Playback speed
    /// </summary>
    /// <value>Playback speed must be greater than zero.</value>
    /// <exception cref="ArgumentOutOfRangeException">Playback speed must be greater than zero.</exception>
    public float PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Playback speed must be greater than zero.");
            _playbackSpeed = value;
        }
    }

    /// <inheritdoc />
    public override string Name { get; set; } = "Player";

    /// <inheritdoc />
    public PlaybackState State { get; private set; }

    /// <inheritdoc />
    public bool IsLooping { get; set; }

    /// <inheritdoc />
    public float Time => (float)_samplePosition / AudioEngine.Channels / AudioEngine.Instance.SampleRate / PlaybackSpeed;

    /// <inheritdoc />
    public float Duration => (float)_dataProvider.Length / AudioEngine.Channels / AudioEngine.Instance.SampleRate / PlaybackSpeed;

    /// <inheritdoc />
    public int LoopStartSamples => _loopStartSamples;
    
    /// <inheritdoc />
    public int LoopEndSamples => _loopEndSamples;

    /// <inheritdoc />
    public float LoopStartSeconds => (float)_loopStartSamples / AudioEngine.Channels / AudioEngine.Instance.SampleRate;

    /// <inheritdoc />
    public float LoopEndSeconds => _loopEndSamples == -1 ? -1 : (float)_loopEndSamples / AudioEngine.Channels / AudioEngine.Instance.SampleRate;

    /// <inheritdoc />
    protected override void GenerateAudio(Span<float> output)
    {
        if (State != PlaybackState.Playing)
            return;
        
        if (IsLooping)
        {
            var loopEnd = _loopEndSamples == -1 ? _dataProvider.Length : _loopEndSamples;
            if (loopEnd > 0 && _samplePosition >= loopEnd)
            {
                Seek(LoopStartSamples);
                _currentFrame = 0f;
                return;
            }
        }

        var channels = AudioEngine.Channels;
        var speed = PlaybackSpeed;
        var outputSampleCount = output.Length;
        var outputFrameCount = outputSampleCount / channels;

        // Calculate the number of source frames required
        var requiredSourceFrames = (int)Math.Ceiling(outputFrameCount * speed) + 2;
        var requiredSourceSamples = requiredSourceFrames * channels;

        var sourceSamples = ArrayPool<float>.Shared.Rent(requiredSourceSamples);
        var sourceSpan = sourceSamples.AsSpan(0, requiredSourceSamples);
        var sourceSamplesRead = _dataProvider.ReadBytes(sourceSpan);

        if (sourceSamplesRead == 0)
        {
            ArrayPool<float>.Shared.Return(sourceSamples);
            HandleEndOfStream(output);
            return;
        }

        var sourceFramesRead = sourceSamplesRead / channels;
        var outputFrameIndex = 0;

        // Process output frames with linear interpolation
        while (outputFrameIndex < outputFrameCount && _currentFrame < sourceFramesRead - 1)
        {
            var sourceFrame = _currentFrame;
            var frameIndex0 = (int)sourceFrame;
            var t = sourceFrame - frameIndex0;

            for (var ch = 0; ch < channels; ch++)
            {
                var sampleIndex0 = frameIndex0 * channels + ch;
                var sampleIndex1 = (frameIndex0 + 1) * channels + ch;

                if (sampleIndex1 >= sourceSamplesRead)
                    break;

                var sample0 = sourceSamples[sampleIndex0];
                var sample1 = sourceSamples[sampleIndex1];
                output[outputFrameIndex * channels + ch] = sample0 * (1 - t) + sample1 * t;
            }

            outputFrameIndex++;
            _currentFrame += speed;
        }

        // Clear any remaining output if underflow occurred.
        if (outputFrameIndex < outputFrameCount)
        {
            output.Slice(outputFrameIndex * channels, (outputFrameCount - outputFrameIndex) * channels).Clear();
        }

        // Update playback position.
        var framesConsumed = (int)_currentFrame;
        _samplePosition += framesConsumed * channels;
        _currentFrame -= framesConsumed;

        ArrayPool<float>.Shared.Return(sourceSamples);

        if (framesConsumed >= sourceFramesRead - 1)
            HandleEndOfStream(output[(outputFrameIndex * channels)..]);
    }

    /// <summary>
    /// Handles the end-of-stream condition.
    /// </summary>
    private void HandleEndOfStream(Span<float> buffer)
    {
        if (IsLooping)
        {
            var loopStart = _loopStartSamples;
            var loopEnd = _loopEndSamples == -1 ? _dataProvider.Length : _loopEndSamples;

            if (loopEnd > 0 && _samplePosition >= loopEnd) // Check if loop end is valid and if current position is at or beyond loop end
            {
                Seek(loopStart); // Seek to the loop start point
            }
            else if (loopEnd <= 0 ) // Loop to start if loopEnd is invalid or not set
            {
                 Seek(loopStart);
            }
            else
            {
                 Seek(loopStart); // Fallback to loop start if something unexpected
            }

            _currentFrame = 0f;
            GenerateAudio(buffer); // Process the buffer again after seeking.
        }
        else
        {
            State = PlaybackState.Stopped;
            OnPlaybackEnded();
            buffer.Clear();
        }
    }

    /// <summary>
    /// Invokes the PlaybackEnded event.
    /// </summary>
    private void OnPlaybackEnded()
    {
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
        if (!IsLooping)
        {
            Enabled = false;
            State = PlaybackState.Stopped;
        }
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
        Pause();
        Seek(0);
    }
    
    /// <summary>
    /// Seek via TimeSpan offset
    /// </summary>
    /// <param name="offset">Offset - amount of time to seek</param>
    /// <param name="seekOrigin">
    /// SeekOrigin - specifies how to calculate the offset
    /// <ul>
    /// <li><b>SeekOrigin.Begin</b>: Beginning of the audio stream</li>
    /// <li><b>SeekOrigin.Current</b>: Current position of the audio stream</li>
    /// <li><b>SeekOrigin.End:</b> Duration of the audio stream (offset has to be negative)</li>
    /// </ul>
    /// </param>
    public void Seek(TimeSpan offset, SeekOrigin seekOrigin = SeekOrigin.Begin)
    {
        var seekOffset = (float)offset.TotalMilliseconds / 1000;
        switch (seekOrigin)
        {
            case SeekOrigin.Current:
                Seek(Time + seekOffset);
                break;
            case SeekOrigin.End:
                Seek(Duration + seekOffset);
                break;
            case SeekOrigin.Begin:
            default:
                Seek(seekOffset);
                break;
        }
    }
    
    /// <inheritdoc />
    public void Seek(float time)
    {
        var sampleOffset = (int)(time / Duration * _dataProvider.Length);
        Seek(sampleOffset);
    }

    /// <inheritdoc />
    public void Seek(int sampleOffset)
    {
        if (!_dataProvider.CanSeek)
            throw new InvalidOperationException("Seeking is not supported for this sound.");

        _dataProvider.Seek(sampleOffset);
        _samplePosition = sampleOffset;
        
        // Reset the fractional frame index for interpolation relative to the new stream position.
        _currentFrame = 0f;
    }

    #endregion
    
    #region Loop Point Configuration Methods

    /// <inheritdoc />
    public void SetLoopPoints(float startTime, float? endTime = -1f)
    {
        if (startTime < 0)
            throw new ArgumentOutOfRangeException(nameof(startTime), "Loop start time cannot be negative.");
        if (endTime.HasValue && Math.Abs(endTime.Value - -1f) < 1e-6 && endTime < startTime)
            throw new ArgumentOutOfRangeException(nameof(endTime), "Loop end time must be greater than or equal to start time, or -1.");

        _loopStartSamples = (int)(startTime * AudioEngine.Instance.SampleRate * AudioEngine.Channels);
        _loopEndSamples = endTime.HasValue ? (Math.Abs(endTime.Value - (-1)) < 1e-6 ? -1 : (int)(endTime.Value * AudioEngine.Instance.SampleRate * AudioEngine.Channels)) : -1;


        // Clamp to valid sample range
        _loopStartSamples = Math.Clamp(_loopStartSamples, 0, _dataProvider.Length);
        _loopEndSamples = _loopEndSamples == -1 ? -1 : Math.Clamp(_loopEndSamples, -1, _dataProvider.Length);
    }

    /// <inheritdoc />
    public void SetLoopPoints(int startSample, int endSample = -1)
    {
        if (startSample < 0)
            throw new ArgumentOutOfRangeException(nameof(startSample), "Loop start sample cannot be negative.");
        if (endSample != -1 && endSample < startSample)
            throw new ArgumentOutOfRangeException(nameof(endSample), "Loop end sample must be greater than or equal to start sample, or -1.");

        _loopStartSamples = startSample;
        _loopEndSamples = endSample;

        // Clamp to valid sample range
        _loopStartSamples = Math.Clamp(_loopStartSamples, 0, _dataProvider.Length);
        _loopEndSamples = _loopEndSamples == -1 ? -1 : Math.Clamp(_loopEndSamples, -1, _dataProvider.Length);
    }

    #endregion
}