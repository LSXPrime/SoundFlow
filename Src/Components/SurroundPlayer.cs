using System.Buffers;
using System.Numerics;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Modifiers;

namespace SoundFlow.Components;

/// <summary>
/// A sound player that simulates surround sound with support for different speaker configurations and advanced panning methods.
/// </summary>
public class SurroundPlayer : SoundComponent, ISoundPlayer
{
    private readonly LowPassModifier _lowPassFilter = new(120f);
    private readonly ISoundDataProvider _dataProvider;
    private int _samplePosition;
    private float _currentFrame;
    private float _playbackSpeed = 1.0f;
    private int _loopStartSamples;
    private int _loopEndSamples = -1;

    /// <inheritdoc />
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
    public override string Name { get; set; } = "Surround Player";

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

    
    /// <summary>
    /// The speaker configuration to use for surround sound.
    /// </summary>
    public enum SpeakerConfiguration
    {
        /// <summary>
        /// Standard stereo configuration with two speakers.
        /// </summary>
        Stereo,

        /// <summary>
        /// Quadraphonic configuration with four speakers.
        /// </summary>
        Quad,

        /// <summary>
        /// 5.1 surround sound configuration with six speakers.
        /// </summary>
        Surround51,

        /// <summary>
        /// 7.1 surround sound configuration with eight speakers.
        /// </summary>
        Surround71,

        /// <summary>
        /// Custom configuration defined by the user.
        /// </summary>
        Custom
    }

    private SpeakerConfiguration _speakerConfig = SpeakerConfiguration.Surround51;

    /// <summary>
    /// The speaker configuration to use for surround sound.
    /// </summary>
    public SpeakerConfiguration SpeakerConfig
    {
        get => _speakerConfig;
        set
        {
            if (value == SpeakerConfiguration.Custom && _currentConfiguration == null)
                throw new InvalidOperationException(
                    "Cannot use Custom speaker configuration without setting a custom SurroundConfig.");

            _speakerConfig = value;
            SetSpeakerConfiguration(value);
        }
    }

    /// <summary>
    /// The panning method to use for surround sound.
    /// </summary>
    public enum PanningMethod
    {
        /// <summary>
        /// Simple linear panning based on speaker position.
        /// </summary>
        Linear,

        /// <summary>
        /// Equal Power panning for smoother transitions.
        /// </summary>
        EqualPower,

        /// <summary>
        /// Vector-Based Amplitude Panning (VBAP).
        /// </summary>
        Vbap
    }

    /// <summary>
    /// The panning method to use for surround sound.
    /// </summary>
    public PanningMethod Panning { get; set; } = PanningMethod.Vbap;

    // VBAP Parameters
    private Vector2 _listenerPosition = Vector2.Zero;

    /// <summary>
    /// Listener position for VBAP panning.
    /// </summary>
    public Vector2 ListenerPosition
    {
        get => _listenerPosition;
        set
        {
            _listenerPosition = value;
            _vbapPanningFactorsDirty = true;
        }
    }

    /// <summary>
    /// VBAP Parameters, used if Panning is set to Vbap.
    /// </summary>
    public VbapParameters VbapParameters { get; set; } = new();

    private SurroundConfiguration? _currentConfiguration;

    /// <summary>
    /// Custom surround sound configuration.
    /// </summary>
    public SurroundConfiguration SurroundConfig
    {
        get => _currentConfiguration ?? throw new InvalidOperationException("No configuration is currently set.");
        set
        {
            if (!value.IsValidConfiguration())
                throw new ArgumentException("Invalid configuration. Make sure all arrays have the same length.");
            
            _currentConfiguration = value;
            _speakerConfig = SpeakerConfiguration.Custom;
            SetSpeakerConfiguration(_speakerConfig);
        }
    }

    // Surround sound parameters (predefined configurations)
    private readonly Dictionary<SpeakerConfiguration, SurroundConfiguration> _predefinedConfigurations = [];

    private float[] _delayLines = [];
    private int[] _delayIndices = [];
    private float[] _panningFactors = [];
    private bool _vbapPanningFactorsDirty = true;

    /// <summary>
    /// A sound player that simulates surround sound with support for different speaker configurations.
    /// </summary>
    public SurroundPlayer(ISoundDataProvider dataProvider)
    {
        _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        InitializePredefinedConfigurations();
        SetSpeakerConfiguration(_speakerConfig);
    }

    private void InitializePredefinedConfigurations()
    {
        //Stereo
        _predefinedConfigurations.Add(SpeakerConfiguration.Stereo, new SurroundConfiguration(
            "Stereo",
            [1f, 1f], // Volumes
            [0f, 0f], // Delays in ms
            [new Vector2(-1f, 0f), new Vector2(1f, 0f)]
        ));

        // Quad
        _predefinedConfigurations.Add(SpeakerConfiguration.Quad, new SurroundConfiguration(
            "Quad",
            [1f, 1f, 0.7f, 0.7f],
            [0f, 0f, 15f, 15f],
            [new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(-1f, -1f), new Vector2(1f, -1f)]
        ));

        // 5.1 Surround
        _predefinedConfigurations.Add(SpeakerConfiguration.Surround51, new SurroundConfiguration(
            "Surround 5.1",
            [1f, 1f, 1f, 0.7f, 0.7f, 0.5f],
            [0f, 0f, 0f, 15f, 15f, 5f],
            [
                new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(-0.8f, -1f),
                new Vector2(0.8f, -1f), new Vector2(0f, -1.5f)
            ]
        ));

        // 7.1 Surround
        _predefinedConfigurations.Add(SpeakerConfiguration.Surround71, new SurroundConfiguration(
            "Surround 7.1",
            [1f, 1f, 1f, 0.7f, 0.7f, 0.7f, 0.7f, 0.5f],
            [0f, 0f, 0f, 15f, 15f, 5f, 5f, 5f],
            [
                new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(-0.8f, -1f),
                new Vector2(0.8f, -1f), new Vector2(-1f, -1.5f), new Vector2(1f, -1.5f), new Vector2(0f, -2f)
            ]
        ));
    }

    /// <summary>
    /// Sets the speaker configuration for surround sound.
    /// </summary>
    /// <param name="config">The speaker configuration to use.</param>
    public void SetSpeakerConfiguration(SpeakerConfiguration config)
    {
        _speakerConfig = config;

        _currentConfiguration = config switch
        {
            SpeakerConfiguration.Custom => _currentConfiguration,
            _ => _predefinedConfigurations.TryGetValue(config, out var predefinedConfig)
                ? predefinedConfig
                : throw new ArgumentException("Invalid speaker configuration.")
        };

        InitializeDelayLines();
        _vbapPanningFactorsDirty = true;
    }

    private void InitializeDelayLines()
    {
        var numChannels = _currentConfiguration!.SpeakerPositions.Length;
        var maxDelaySamples = (int)(_currentConfiguration.Delays.Max() * AudioEngine.Instance.SampleRate / 1000f);
        _delayLines = new float[numChannels * (maxDelaySamples + 1)];
        _delayIndices = new int[numChannels];
    }

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

        // Read source samples with speed consideration
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

        // Resample with linear interpolation
        while (outputFrameIndex < outputFrameCount && _currentFrame < sourceFramesRead - 1)
        {
            var sourceFrame = _currentFrame;
            var frameIndex0 = (int)sourceFrame;
            var t = sourceFrame - frameIndex0;

            for (var ch = 0; ch < channels; ch++)
            {
                var sampleIndex0 = frameIndex0 * channels + ch;
                var sampleIndex1 = (frameIndex0 + 1) * channels + ch;

                if (sampleIndex1 >= sourceSamplesRead) break;

                var sample0 = sourceSamples[sampleIndex0];
                var sample1 = sourceSamples[sampleIndex1];
                output[outputFrameIndex * channels + ch] = sample0 * (1 - t) + sample1 * t;
            }

            outputFrameIndex++;
            _currentFrame += speed;
        }

        // Process remaining output if underflow
        if (outputFrameIndex < outputFrameCount)
            output.Slice(outputFrameIndex * channels, (outputFrameCount - outputFrameIndex) * channels).Clear();

        // Update sample position and handle looping
        var framesConsumed = (int)_currentFrame;
        _samplePosition += framesConsumed * channels;
        _currentFrame -= framesConsumed;

        ArrayPool<float>.Shared.Return(sourceSamples);

        // Apply surround processing to the resampled audio
        ProcessSurroundAudio(output);

        // Check for end of stream
        if (framesConsumed >= sourceFramesRead - 1)
            HandleEndOfStream(output[(outputFrameIndex * channels)..]);
    }
    
    private void ProcessSurroundAudio(Span<float> buffer)
    {
        UpdatePanningFactors();

        for (var i = 0; i < buffer.Length; i++)
        {
            var outputSample = 0f;
            var numChannels = _currentConfiguration!.SpeakerPositions.Length;

            for (var speakerIndex = 0; speakerIndex < numChannels; speakerIndex++)
            {
                if (speakerIndex == numChannels - 1 && _speakerConfig != SpeakerConfiguration.Stereo)
                    buffer[i] = ApplyLowPassFilter(buffer[i]);

                outputSample += ApplyDelayAndVolume(buffer[i],
                    _currentConfiguration.Volumes[speakerIndex] * _panningFactors[speakerIndex],
                    _currentConfiguration.Delays[speakerIndex], speakerIndex);
            }

            buffer[i] = outputSample;
        }
    }
    
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
            else if (loopEnd <= 0) // Loop to start if loopEnd is invalid or not set
            {
                Seek(loopStart);
            }
            else
            {
                Seek(loopStart); // Fallback to loop start if something unexpected
            }

            _currentFrame = 0;
            GenerateAudio(buffer);
        }
        else
        {
            State = PlaybackState.Stopped;
            OnPlaybackEnded();
            buffer.Clear();
        }
    }

    private void UpdatePanningFactors()
    {
        switch (Panning)
        {
            case PanningMethod.Linear:
                _panningFactors = CalculateLinearPanningFactors();
                break;
            case PanningMethod.EqualPower:
                _panningFactors = CalculateEqualPowerPanningFactors();
                break;
            case PanningMethod.Vbap:
            default:
                RecalculateVbapPanningFactorsIfNecessary();
                break;
        }
    }

    private float[] CalculateLinearPanningFactors()
    {
        var numChannels = _currentConfiguration!.SpeakerPositions.Length;
        var panningFactors = new float[numChannels];
        var listenerPos = ListenerPosition;
        var totalDistance = 0f;

        // Sum of inverse distances as panning weights
        for (var i = 0; i < numChannels; i++)
            totalDistance += 1f / Vector2.Distance(listenerPos, _currentConfiguration.SpeakerPositions[i]);

        // Invert distance and normalize the sum. So further away the audio is less of that channel
        for (var i = 0; i < numChannels; i++)
        {
            var distance = Vector2.Distance(listenerPos, _currentConfiguration.SpeakerPositions[i]);
            if (distance < float.Epsilon)
                panningFactors[i] = 1f;
            else panningFactors[i] = 1f / distance / totalDistance;
        }

        return panningFactors;
    }

    private float[] CalculateEqualPowerPanningFactors()
    {
        var numChannels = _currentConfiguration!.SpeakerPositions.Length;
        var panningFactors = new float[numChannels];
        var listenerPos = ListenerPosition;

        for (var i = 0; i < numChannels; i++)
        {
            var dx = _currentConfiguration.SpeakerPositions[i].X - listenerPos.X;
            var dy = _currentConfiguration.SpeakerPositions[i].Y - listenerPos.Y;

            //Calculate angle
            var angle = MathF.Atan2(dy, dx);

            // Map angle from -pi to pi into a range 0 to 1 and clamp it between
            var normalizedAngle = (angle / MathF.PI + 1f) / 2f;

            // Constant power panning function, panningAudio is panned according to the angle from the listener
            var leftPanning = MathF.Cos(normalizedAngle * MathF.PI / 2f);
            var rightPanning = MathF.Sin(normalizedAngle * MathF.PI / 2f);

            // Assign appropriate weights based on channel
            panningFactors[i] = i % 2 == 0 ? leftPanning : rightPanning;
        }

        return panningFactors;
    }

    private void RecalculateVbapPanningFactorsIfNecessary()
    {
        if (!_vbapPanningFactorsDirty) 
            return;
        _panningFactors = CalculateVbapPanningFactors();
        _vbapPanningFactorsDirty = false;
    }

    private float[] CalculateVbapPanningFactors()
    {
        var numChannels = _currentConfiguration!.SpeakerPositions.Length;
        var panningFactors = new float[numChannels];
        var listenerPos = (ListenerPosition.X, ListenerPosition.Y);
        var speakerPositions = _currentConfiguration.SpeakerPositions;

        for (var speakerIndex = 0; speakerIndex < numChannels; speakerIndex++)
            panningFactors[speakerIndex] = CalculateVbapPanningFactor(speakerPositions[speakerIndex], listenerPos);

        return panningFactors;
    }

    private float CalculateVbapPanningFactor(Vector2 speakerPos, (float x, float y) listenerPos)
    {
        var dx = speakerPos.X - listenerPos.x;
        var dy = speakerPos.Y - listenerPos.y;

        // Calculate squared distance, including min distance to avoid singularities.
        var distanceSquared = MathF.Max(VbapParameters.MinDistance * VbapParameters.MinDistance, dx * dx + dy * dy);

        // Use inverse square law for more realistic attenuation
        var distanceAttenuation = 1f / (1f + VbapParameters.RolloffFactor * distanceSquared);

        // Calculate angle and spread
        var angle = MathF.Atan2(dy, dx);
        if (angle < 0)
            angle += 2 * MathF.PI;

        var spreadAngle = angle * VbapParameters.Spread;

        // Panning Algorithm (e.g., constant power panning)
        var panningFactor = MathF.Sqrt(distanceAttenuation);
        panningFactor *= MathF.Cos(spreadAngle / 2);

        return panningFactor;
    }

    private float ApplyDelayAndVolume(float sample, float volume, float delayMs, int speakerIndex)
    {
        var delaySamples = (int)(delayMs * AudioEngine.Instance.SampleRate / 1000f);

        var delayIndex = (_delayIndices[speakerIndex] - delaySamples + _delayLines.Length) % _delayLines.Length;
        var delayedSample = _delayLines[delayIndex];

        _delayLines[_delayIndices[speakerIndex]] = sample;
        _delayIndices[speakerIndex] = (_delayIndices[speakerIndex] + 1) % _delayLines.Length;

        return delayedSample * volume;
    }

    private float ApplyLowPassFilter(float sample)
    {
        return _lowPassFilter.ProcessSample(sample, 0);
    }

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
    ///     Occurs when playback ends.
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

    /// <inheritdoc />
    public void Seek(float time)
    {
        var sampleOffset = (int)(time * AudioEngine.Instance.SampleRate * AudioEngine.Channels);
        Seek(sampleOffset);
    }

    /// <inheritdoc />
    public void Seek(int sampleOffset)
    {
        if (!_dataProvider.CanSeek)
            throw new InvalidOperationException("Seeking is not supported for this sound.");

        _dataProvider.Seek(sampleOffset);
        _samplePosition = sampleOffset;
        _currentFrame = 0;
        InitializeDelayLines();
    }

    #endregion

    #region Loop Point Configuration Methods & Properties

    /// <summary>
    /// Sets the loop points for the sound player in seconds.
    /// </summary>
    /// <param name="startTime">The loop start time in seconds. Must be non-negative.</param>
    /// <param name="endTime">The loop end time in seconds, optional. Use -1 or null to loop to the natural end of the audio. Must be greater than or equal to startTime, or -1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if startTime is negative or endTime is invalid.</exception>
    public void SetLoopPoints(float startTime, float? endTime = -1f)
    {
        if (startTime < 0)
            throw new ArgumentOutOfRangeException(nameof(startTime), "Loop start time cannot be negative.");
        if (endTime.HasValue && endTime != -1 && endTime < startTime)
            throw new ArgumentOutOfRangeException(nameof(endTime), "Loop end time must be greater than or equal to start time, or -1.");

        _loopStartSamples = (int)(startTime * AudioEngine.Instance.SampleRate * AudioEngine.Channels);
        _loopEndSamples = endTime.HasValue ? (endTime == -1 ? -1 : (int)(endTime.Value * AudioEngine.Instance.SampleRate * AudioEngine.Channels)) : -1;


        // Clamp to valid sample range
        _loopStartSamples = Math.Clamp(_loopStartSamples, 0, _dataProvider.Length);
        _loopEndSamples = _loopEndSamples == -1 ? -1 : Math.Clamp(_loopEndSamples, -1, _dataProvider.Length);
    }

    /// <summary>
    /// Sets the loop points for the sound player in samples.
    /// </summary>
    /// <param name="startSample">The loop start sample. Must be non-negative.</param>
    /// <param name="endSample">The loop end sample, optional. Use -1 or null to loop to the natural end of the audio. Must be greater than or equal to startSample, or -1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if startSample is negative or endSample is invalid.</exception>
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


/// <summary>
///     Configuration for a surround sound.
/// </summary>
/// <param name="name">The name of the configuration.</param>
/// <param name="volumes">The volumes for each speaker.</param>
/// <param name="delays">The delays for each speaker.</param>
/// <param name="speakerPositions">The positions of each speaker.</param>
public class SurroundConfiguration(string name, float[] volumes, float[] delays, Vector2[] speakerPositions)
{
    /// <summary>
    ///     The name of the configuration.
    /// </summary>
    public string Name { get; set; } = name;
    
    /// <summary>
    ///     The volumes for each speaker.
    /// </summary>
    public float[] Volumes { get; set; } = volumes;
    
    /// <summary>
    ///     The delays for each speaker.
    /// </summary>
    public float[] Delays { get; set; } = delays;
    
    /// <summary>
    ///     The positions of each speaker.
    /// </summary>
    public Vector2[] SpeakerPositions { get; set; } = speakerPositions;

    /// <summary>
    /// Validate that all arrays have the same length to help ensure everything will run smoothly
    /// </summary>
    /// <returns>True if the configuration is valid</returns>
    public bool IsValidConfiguration()
    {
        var numSpeakers = SpeakerPositions.Length;
        return Volumes.Length == numSpeakers && Delays.Length == numSpeakers;
    }
}

/// <summary>
///     Parameters for VBAP panning.
/// </summary>
public class VbapParameters
{
    /// <summary>
    /// Rolloff factor for VBAP panning.
    /// </summary>
    /// <remarks>Default value is 1.</remarks>
    public float RolloffFactor { get; set; } = 1f;

    /// <summary>
    /// Minimum distance for VBAP panning to avoid singularities.
    /// </summary>
    /// <remarks>Default value is 0.1.</remarks>
    public float MinDistance { get; set; } = 0.1f;

    /// <summary>
    /// Spread factor for VBAP panning.
    /// </summary>
    /// <remarks>Options: 1 (natural), more than 1 (wider), less than 1 (narrower).</remarks>
    public float Spread { get; set; } = 1f;
}