using System.Buffers;
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

    private float[][] _delayLines = []; // 2D array for isolated delay buffers per speaker
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
            [1f, 1f, 1f, 0.7f, 0.7f, 0.5f], // L, R, C, SL, SR, LFE
            [0f, 0f, 0f, 15f, 15f, 5f],
            [
                new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(-0.8f, -1f),
                new Vector2(0.8f, -1f), new Vector2(0f, -1.5f)
            ])
            {
                LfeChannelIndex = 5
            }
        );

        // 7.1 Surround
        _predefinedConfigurations.Add(SpeakerConfiguration.Surround71, new SurroundConfiguration(
            "Surround 7.1",
            [1f, 1f, 1f, 0.7f, 0.7f, 0.7f, 0.7f, 0.5f], // L, R, C, SL, SR, SideL, SideR, LFE
            [0f, 0f, 0f, 15f, 15f, 5f, 5f, 5f],
            [
                new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(-0.8f, -1f),
                new Vector2(0.8f, -1f), new Vector2(-1f, -1.5f), new Vector2(1f, -1.5f), new Vector2(0f, -2f)
            ])
            {
                LfeChannelIndex = 7
            }
        );
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
        var numSpeakers = _currentConfiguration.SpeakerPositions.Length;
        _delayLines = new float[numSpeakers][];
        _delayIndices = new int[numSpeakers];

        for (var i = 0; i < numSpeakers; i++)
        {
            var delaySamples = (int)(_currentConfiguration.Delays[i] * Format.SampleRate / 1000f);
            // Each speaker gets its own dedicated buffer. Add 1 for safety with zero-length delays.
            _delayLines[i] = new float[delaySamples + 1];
        }
    }

    /// <inheritdoc />
    protected override void GenerateAudio(Span<float> output, int channels)
    {
        base.GenerateAudio(output, channels);
        ProcessSurroundAudio(output, channels);
    }

    private void ProcessSurroundAudio(Span<float> buffer, int outputChannels)
    {
        var sourceLayout = Format.Layout;

        if (sourceLayout is ChannelLayout.Mono or ChannelLayout.Stereo)
        {
            UpdatePanningFactors(outputChannels);
        }

        var frameCount = buffer.Length / outputChannels;
        var sourceChannels = Format.Channels;
        
        var tempFrame = ArrayPool<float>.Shared.Rent(outputChannels);
        var sourceFrame = ArrayPool<float>.Shared.Rent(sourceChannels);

        try
        {
            for (var frame = 0; frame < frameCount; frame++)
            {
                var frameStart = frame * outputChannels;
                var sourceFrameStart = frame * sourceChannels;

                // Copy the original source frame for processing
                buffer.Slice(sourceFrameStart, sourceChannels).CopyTo(sourceFrame);
                
                // Clear the temporary frame buffer for new data
                var tempFrameSpan = tempFrame.AsSpan(0, outputChannels);
                tempFrameSpan.Clear();

                // Map source channels to output channels, performing upmixing or downmixing as needed.
                MapChannels(sourceFrame.AsSpan(0, sourceChannels), tempFrameSpan, sourceLayout);
                
                // Clear the current frame's output in the main buffer before writing new data.
                buffer.Slice(frameStart, outputChannels).Clear();

                // If the source is simple (Mono/Stereo), we pan it across the virtual speaker array.
                // For complex sources (Quad, 5.1 etc.), the upmixed/downmixed result from MapChannels is used directly.
                if (sourceLayout is ChannelLayout.Mono or ChannelLayout.Stereo)
                {
                    // For stereo, we pan the downmixed mono signal. For true stereo placement, separate SoundPlayers would be used.
                    var inputSample = (tempFrame[0] + tempFrame[1]) * 0.5f;

                    for (var speakerIndex = 0; speakerIndex < _currentConfiguration.SpeakerPositions.Length; speakerIndex++)
                    {
                        var delayedSample = ApplyDelayAndVolume(
                            inputSample,
                            _currentConfiguration.Volumes[speakerIndex],
                            _currentConfiguration.Delays[speakerIndex],
                            speakerIndex
                        );
                        
                        // Apply low-pass filter to the virtual LFE speaker if it exists.
                        if (speakerIndex == _currentConfiguration.LfeChannelIndex)
                        {
                            delayedSample = ApplyLowPassFilter(delayedSample);
                        }
                        
                        // Distribute the delayed sample to each output channel based on panning factors.
                        for (var ch = 0; ch < outputChannels; ch++)
                        {
                            buffer[frameStart + ch] += delayedSample * _panningFactors[speakerIndex][ch];
                        }
                    }
                }
                else 
                {
                    // Apply LFE low-pass filter directly to the mapped multi-channel frame.
                    if (_currentConfiguration.LfeChannelIndex >= 0 && _currentConfiguration.LfeChannelIndex < outputChannels)
                    {
                        var lfeIndex = _currentConfiguration.LfeChannelIndex;
                        tempFrame[lfeIndex] = ApplyLowPassFilter(tempFrame[lfeIndex]);
                    }
                    
                    // For Quad, Surround sources, copy the mapped (upmixed/downmixed) channels directly.
                    tempFrameSpan.CopyTo(buffer.Slice(frameStart, outputChannels));
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(tempFrame);
            ArrayPool<float>.Shared.Return(sourceFrame);
        }
    }

    /// <summary>
    /// Maps input channels from a source layout to the output buffer based on the player's target speaker configuration.
    /// Handles upmixing from mono/stereo, downmixing from surround, and direct mapping scenarios.
    /// </summary>
    private void MapChannels(ReadOnlySpan<float> sourceFrame, Span<float> outputFrame, ChannelLayout sourceLayout)
    {
        // Standard channel indices for 5.1/7.1 SMPTE/ITU layout
        const int L = 0, R = 1, C = 2, LFE = 3, SL = 4, SR = 5, SideL = 6, SideR = 7;

        switch (sourceLayout)
        {
            case ChannelLayout.Mono:
                // For panning, we just need a single value since the actual placement happens in the main loop.
                // We place it in the first channel of the temp buffer as a convention.
                if (outputFrame.Length > 0)
                {
                    outputFrame[0] = sourceFrame[0];
                }
                break;

            case ChannelLayout.Stereo:
                // For panning, place L/R in the first two temp channels. The main loop will downmix this for panning.
                if (outputFrame.Length >= 2)
                {
                    outputFrame[L] = sourceFrame[L];
                    outputFrame[R] = sourceFrame[R];
                }
                else if (outputFrame.Length == 1)
                {
                    outputFrame[0] = (sourceFrame[L] + sourceFrame[R]) * 0.5f;
                }
                break;

            case ChannelLayout.Surround51:
                if (_speakerConfig == SpeakerConfiguration.Stereo && outputFrame.Length >= 2)
                {
                    // Downmix 5.1 to Stereo using standard coefficients
                    outputFrame[L] = sourceFrame[L] + sourceFrame[C] * 0.707f + sourceFrame[SL] * 0.707f;
                    outputFrame[R] = sourceFrame[R] + sourceFrame[C] * 0.707f + sourceFrame[SR] * 0.707f;
                }
                else if (outputFrame.Length >= 6) // Direct map or upmix to 7.1
                {
                    // Copy base 5.1 channels
                    sourceFrame.Slice(0, 6).CopyTo(outputFrame);
                    // If output is 7.1, duplicate surround rears to side channels
                    if (outputFrame.Length >= 8)
                    {
                        outputFrame[SideL] = sourceFrame[SL];
                        outputFrame[SideR] = sourceFrame[SR];
                    }
                }
                break;

            case ChannelLayout.Surround71:
                if (_speakerConfig == SpeakerConfiguration.Stereo && outputFrame.Length >= 2)
                {
                    // Downmix 7.1 to Stereo using standard coefficients
                    outputFrame[L] = sourceFrame[L] + sourceFrame[C] * 0.707f + sourceFrame[SL] * 0.5f + sourceFrame[SideL] * 0.866f;
                    outputFrame[R] = sourceFrame[R] + sourceFrame[C] * 0.707f + sourceFrame[SR] * 0.5f + sourceFrame[SideR] * 0.866f;
                }
                else if (_speakerConfig == SpeakerConfiguration.Surround51 && outputFrame.Length >= 6)
                {
                    // Downmix 7.1 to 5.1 by mixing side channels into surrounds
                    outputFrame[L] = sourceFrame[L];
                    outputFrame[R] = sourceFrame[R];
                    outputFrame[C] = sourceFrame[C];
                    outputFrame[LFE] = sourceFrame[LFE];
                    outputFrame[SL] = sourceFrame[SL] + sourceFrame[SideL] * 0.707f;
                    outputFrame[SR] = sourceFrame[SR] + sourceFrame[SideR] * 0.707f;
                }
                else if (outputFrame.Length >= 8) // Direct map to 7.1
                {
                    sourceFrame.CopyTo(outputFrame);
                }
                break;

            default: // Unknown or Quad, just copy what fits
                var toCopy = Math.Min(sourceFrame.Length, outputFrame.Length);
                sourceFrame.Slice(0, toCopy).CopyTo(outputFrame);
                break;
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
            if (sum > 0)
            {
                for (var i = 0; i < weights.Length; i++)
                    weights[i] /= sum;
            }

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
        var speakerDelayLine = _delayLines[speakerIndex];
        // If there's no delay buffer for this speaker, just apply volume and return.
        if (speakerDelayLine.Length <= 1)
        {
            return sample * volume;
        }

        var writeIndex = _delayIndices[speakerIndex];

        // Read the oldest sample from the current write position (before we overwrite it).
        var delayedSample = speakerDelayLine[writeIndex];

        // Write the new, incoming sample into the delay line.
        speakerDelayLine[writeIndex] = sample;

        // Advance the write pointer, wrapping it around the buffer for this specific speaker.
        _delayIndices[speakerIndex] = (writeIndex + 1) % speakerDelayLine.Length;

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
    /// The index of the LFE (Low-Frequency Effects) channel in this configuration.
    /// Set to -1 if no LFE channel exists.
    /// </summary>
    public int LfeChannelIndex { get; set; } = -1;

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