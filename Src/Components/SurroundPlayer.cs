using System.Numerics;
using SoundFlow.Abstracts;
using SoundFlow.Interfaces;
using SoundFlow.Modifiers;
using SoundFlow.Structs;

namespace SoundFlow.Components;

/// <summary>
/// A sound player that plays audio in a surround sound configuration.
/// </summary>
public sealed class SurroundPlayer : SoundPlayerBase
{
    private readonly LowPassModifier _lowPassFilter;

    /// <inheritdoc />
    public override string Name { get; set; } = "Surround Player";

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

    private SurroundConfiguration _currentConfiguration = null!;

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
    private readonly Dictionary<SpeakerConfiguration, SurroundConfiguration> _predefinedConfigurations = new();

    private float[] _delayLines = [];
    private int[] _delayIndices = [];
    private float[][] _panningFactors = []; // 2D array of [virtualSpeaker][outputChannel]
    private bool _vbapPanningFactorsDirty = true;

    /// <summary>
    /// A sound player that simulates surround sound with support for different speaker configurations.
    /// </summary>
    /// <param name="engine">The audio engine used for managing audio playback.</param>
    /// <param name="format">The format of the audio stream, including sample rate, sample format, and channel count.</param>
    /// <param name="dataProvider">The data provider that supplies audio data for playback.</param>
    public SurroundPlayer(AudioEngine engine, AudioFormat format, ISoundDataProvider dataProvider) : base(engine, format, dataProvider)
    {
        _lowPassFilter = new LowPassModifier(format, 120f);
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
        var numChannels = _currentConfiguration.SpeakerPositions.Length;
        var maxDelaySamples = (int)(_currentConfiguration.Delays.Max() * Format.SampleRate / 1000f);
        _delayLines = new float[numChannels * (maxDelaySamples + 1)];
        _delayIndices = new int[numChannels];
    }

    /// <inheritdoc />
    protected override void GenerateAudio(Span<float> output, int channels)
    {
        base.GenerateAudio(output, channels);
        ProcessSurroundAudio(output, channels);
    }

    private void ProcessSurroundAudio(Span<float> buffer, int channels)
    {
        UpdatePanningFactors(channels);

        var frameCount = buffer.Length / channels;

        for (var frame = 0; frame < frameCount; frame++)
        {
            // Assuming base audio is mono
            // TODO: refactor when support for getting audio data is added (e.g., mono, stereo, 5.1 or 7.1, etc.)
            var inputSample = buffer[frame * channels];

            // down-mixing stereo to mono
            if (channels >= 2)
            {
                var left = buffer[frame * channels];
                var right = buffer[frame * channels + 1];
                inputSample = (left + right) / 2;
            }

            // Clear the current frame's output
            for (var ch = 0; ch < channels; ch++)
            {
                buffer[frame * channels + ch] = 0f;
            }

            // Process each virtual speaker
            for (var speakerIndex = 0; speakerIndex < _currentConfiguration.SpeakerPositions.Length; speakerIndex++)
            {
                var delayedSample = ApplyDelayAndVolume(
                    inputSample,
                    _currentConfiguration.Volumes[speakerIndex],
                    _currentConfiguration.Delays[speakerIndex],
                    speakerIndex
                );

                // Apply low-pass filter to LFE channel (e.g., last speaker in 5.1)
                if (speakerIndex == _currentConfiguration.SpeakerPositions.Length - 1 &&
                    _speakerConfig != SpeakerConfiguration.Stereo)
                {
                    delayedSample = ApplyLowPassFilter(delayedSample);
                }

                // Distribute the delayed sample to each output channel based on panning factors
                for (var ch = 0; ch < channels; ch++)
                {
                    buffer[frame * channels + ch] += delayedSample * _panningFactors[speakerIndex][ch];
                }
            }
        }
    }

    /// <inheritdoc />
    protected override void HandleEndOfStream(Span<float> buffer, int channels)
    {
        base.HandleEndOfStream(buffer, channels);
        InitializeDelayLines(); // Re-initialize delay lines on loop or stop to avoid artifacts.
    }


    private void UpdatePanningFactors(int channels)
    {
        switch (Panning)
        {
            case PanningMethod.Linear:
                _panningFactors = CalculateLinearPanningFactors(channels);
                break;
            case PanningMethod.EqualPower:
                _panningFactors = CalculateEqualPowerPanningFactors(channels);
                break;
            case PanningMethod.Vbap:
            default:
                RecalculateVbapPanningFactorsIfNecessary(channels);
                break;
        }
    }

    private float[][] CalculateLinearPanningFactors(int channels)
    {
        var numVirtualSpeakers = _currentConfiguration.SpeakerPositions.Length;
        var numOutputChannels = channels;
        var factors = new float[numVirtualSpeakers][];

        // Get physical output speaker positions
        var outputSpeakerPositions = GetOutputSpeakerLayout(numOutputChannels);

        for (var vsIdx = 0; vsIdx < numVirtualSpeakers; vsIdx++)
        {
            factors[vsIdx] = new float[numOutputChannels];
            var virtualPos = _currentConfiguration.SpeakerPositions[vsIdx];
            var relativeVec = virtualPos - _listenerPosition;

            // Calculate weights based on inverse distance to output speakers
            var totalWeight = 0f;
            var distances = new float[numOutputChannels];

            for (var ch = 0; ch < numOutputChannels; ch++)
            {
                var distance = Vector2.Distance(relativeVec,
                    outputSpeakerPositions[ch] - _listenerPosition);
                distances[ch] = distance;
                totalWeight += 1f / (distance + 0.001f); // Prevent division by zero
            }

            // Assign weights inversely proportional to distance
            for (var ch = 0; ch < numOutputChannels; ch++)
            {
                factors[vsIdx][ch] = (1f / (distances[ch] + 0.001f)) / totalWeight;
            }
        }

        return factors;
    }

    private float[][] CalculateEqualPowerPanningFactors(int channels)
    {
        var numSpeakers = _currentConfiguration.SpeakerPositions.Length;
        var numOutputChannels = channels;
        var factors = new float[numSpeakers][];

        var outputSpeakers = GetOutputSpeakerLayout(numOutputChannels);

        for (var vsIdx = 0; vsIdx < numSpeakers; vsIdx++)
        {
            factors[vsIdx] = new float[numOutputChannels];
            var virtualPos = _currentConfiguration.SpeakerPositions[vsIdx];
            var relativeVec = virtualPos - _listenerPosition;
            var distance = relativeVec.Length();
            var direction = relativeVec / distance;

            // Calculate angles between virtual source and all output speakers
            var angles = new float[numOutputChannels];
            var total = 0f;

            for (var ch = 0; ch < numOutputChannels; ch++)
            {
                var spkDir = Vector2.Normalize(outputSpeakers[ch] - _listenerPosition);
                var dot = Vector2.Dot(direction, spkDir);
                angles[ch] = MathF.Acos(Math.Clamp(dot, -1, 1));
                total += 1f / (angles[ch] + 0.001f); // Avoid division by zero
            }

            // Calculate inverse-angle weighted distribution
            for (var ch = 0; ch < numOutputChannels; ch++)
            {
                var weight = (1f / (angles[ch] + 0.001f)) / total;
                factors[vsIdx][ch] = weight * (1f / (1 + VbapParameters.RolloffFactor * distance));
            }
        }

        return factors;
    }

    private void RecalculateVbapPanningFactorsIfNecessary(int channels)
    {
        if (!_vbapPanningFactorsDirty)
            return;
        _panningFactors = CalculateVbapPanningFactors(channels);
        _vbapPanningFactorsDirty = false;
    }

    private float[][] CalculateVbapPanningFactors(int channels)
    {
        var numVirtualSpeakers = _currentConfiguration.SpeakerPositions.Length;
        var numOutputChannels = channels;
        var factors = new float[numVirtualSpeakers][];

        // Get output speaker positions (base positions on current channel count)
        var outputSpeakerPositions = GetOutputSpeakerLayout(channels);

        for (var vsIdx = 0; vsIdx < numVirtualSpeakers; vsIdx++)
        {
            factors[vsIdx] = new float[numOutputChannels];
            var virtualPos = _currentConfiguration.SpeakerPositions[vsIdx];

            // Calculate relative vector from listener to virtual speaker
            var relativeVec = virtualPos - _listenerPosition;
            var distance = relativeVec.Length();
            var direction = relativeVec / distance;

            // Find the triangle of output speakers that contains the virtual speaker
            var weights = CalculateVbapWeights(direction, outputSpeakerPositions);

            // Apply distance attenuation and normalize
            var attenuation = 1f / (1 + VbapParameters.RolloffFactor * distance);

            for (var ch = 0; ch < numOutputChannels; ch++)
            {
                factors[vsIdx][ch] = weights[ch] * attenuation;
            }
        }

        return factors;
    }

    private float[] CalculateVbapWeights(Vector2 direction, Vector2[] outputSpeakers)
    {
        var numSpeakers = outputSpeakers.Length;
        var weights = new float[numSpeakers];
        var maxContribution = -1f;

        for (var a = 0; a < numSpeakers; a++)
        {
            var spkA = Vector2.Normalize(outputSpeakers[a] - _listenerPosition);

            for (var b = a + 1; b < numSpeakers; b++)
            {
                var spkB = Vector2.Normalize(outputSpeakers[b] - _listenerPosition);

                // Calculate determinant for orientation check
                var det = spkA.X * spkB.Y - spkB.X * spkA.Y;
                if (MathF.Abs(det) < 1e-6) continue;

                // Calculate barycentric coordinates
                var wa = (direction.X * spkB.Y - direction.Y * spkB.X) / det;
                var wb = (direction.Y * spkA.X - direction.X * spkA.Y) / det;

                if (wa >= 0 && wb >= 0 && (wa + wb) <= 1)
                {
                    // Calculate actual contribution strength
                    var contribution = wa * Vector2.Dot(direction, spkA) +
                                       wb * Vector2.Dot(direction, spkB);

                    if (contribution > maxContribution)
                    {
                        maxContribution = contribution;
                        Array.Clear(weights, 0, weights.Length);
                        weights[a] = wa;
                        weights[b] = wb;
                    }
                }
            }
        }

        // Normalize if valid weights found
        if (maxContribution > 0)
        {
            var sum = weights.Sum();
            for (var i = 0; i < weights.Length; i++)
                weights[i] /= sum;

            return weights;
        }

        // Fallback: Find nearest speaker
        var maxDot = -1f;
        var nearest = 0;
        for (var i = 0; i < numSpeakers; i++)
        {
            var dot = Vector2.Dot(direction,
                Vector2.Normalize(outputSpeakers[i] - _listenerPosition));
            if (dot > maxDot)
            {
                maxDot = dot;
                nearest = i;
            }
        }

        weights[nearest] = 1f;
        return weights;
    }

    private Vector2[] GetOutputSpeakerLayout(int channelCount)
    {
        // Define standard speaker layouts based on channel count
        return channelCount switch
        {
            1 => [new Vector2(0, 0)], // Mono
            2 => [new Vector2(-1, 0), new Vector2(1, 0)], // Stereo
            4 =>
            [ // Quad
                new Vector2(-1, 0), new Vector2(1, 0),
                new Vector2(0, 1), new Vector2(0, -1)
            ],
            5 =>
            [ // 5.0 surround
                new Vector2(-1, 0), new Vector2(1, 0), // Front L/R
                new Vector2(0, 0), // Center
                new Vector2(-0.5f, -1), new Vector2(0.5f, -1) // Rear L/R
            ],
            6 =>
            [ // 5.1 surround
                new Vector2(-1, 0), new Vector2(1, 0), // Front L/R
                new Vector2(0, 0), // Center
                new Vector2(-0.5f, -1), new Vector2(0.5f, -1), // Rear L/R
                new Vector2(0, -1.5f) // LFE
            ],
            8 =>
            [ // 7.1 surround
                new Vector2(-1, 0), new Vector2(1, 0), // Front L/R
                new Vector2(0, 0), // Center
                new Vector2(-1, -1), new Vector2(1, -1), // Side L/R
                new Vector2(-0.5f, -1.5f), new Vector2(0.5f, -1.5f), // Rear L/R
                new Vector2(0, -2f) // LFE
            ],
            _ => CreateCircularLayout(channelCount) // Fallback for unknown configs
        };
    }

    private Vector2[] CreateCircularLayout(int speakers)
    {
        var positions = new Vector2[speakers];
        var angleStep = 2 * MathF.PI / speakers;

        for (var i = 0; i < speakers; i++)
        {
            var angle = i * angleStep;
            positions[i] = new Vector2(
                MathF.Cos(angle),
                MathF.Sin(angle)
            );
        }

        return positions;
    }

    private float ApplyDelayAndVolume(float sample, float volume, float delayMs, int speakerIndex)
    {
        var delaySamples = (int)(delayMs * Format.SampleRate / 1000f);

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

    #region Audio Playback Control

    /// <summary>
    /// Seeks to a specific sample offset in the audio playback.
    /// </summary>
    /// <param name="sampleOffset">The sample offset to seek to, relative to the beginning of the audio data.</param>
    public new bool Seek(int sampleOffset)
    {
        var result = base.Seek(sampleOffset);
        if (result)
            InitializeDelayLines(); // Re-initialize delay lines when seeking.
        return result;
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
