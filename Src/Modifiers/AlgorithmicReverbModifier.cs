using SoundFlow.Abstracts;
using System.Runtime.CompilerServices;

namespace SoundFlow.Modifiers;

/// <summary>
/// Free-verb algorithmic reverb modifier.
/// </summary>
public sealed class AlgorithmicReverbModifier : SoundModifier
{
    private const int NumCombs = 8;
    private const int NumAllPasses = 4;

    // Comb filters (indexed by channel, then by comb filter index)
    private CombFilter[][] _combFilters;

    // All-pass filters (indexed by channel, then by all-pass filter index)
    private AllPassFilter[][] _allPassFilters;

    private float _wet = 0.5f; // Wet/dry mix (0-1)
    private float _roomSize = 0.5f; // Room size (0-1)
    private float _damp = 0.5f; // Damping (0-1)
    private float _width = 1f; // Stereo width (0-1) - Now used for multichannel spread
    private float _preDelay; // Pre-delay time (in milliseconds)
    private float _mix = 0.5f; // Early reflection / reverb tail mix (0-1)
    private int _preDelaySamples;
    private float[][] _preDelayBuffers; // Pre-delay buffer per channel
    private int[] _preDelayIndices;

    // Modulation
    private const float ModulationRate = 0.1f; // Modulation rate in Hz (fixed for now)
    private const float ModulationDepth = 0.005f; // Modulation depth
    private float[] _modulatedCombTuning;
    private float[] _lfoPhase;

    /// <inheritdoc />
    public override string Name { get; set; } = "Free-verb Algorithmic Reverb";

    // Default values for filter parameters (per channel)
    private static readonly float[][] CombTunings =
    [
        [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617], // Channel 0
        [1139, 1211, 1298, 1379, 1445, 1514, 1580, 1640], // Channel 1
        [1150, 1222, 1311, 1392, 1460, 1529, 1597, 1657], // Channel 2 
        [1163, 1235, 1324, 1405, 1475, 1544, 1614, 1674], // Channel 3
        [1176, 1248, 1337, 1418, 1490, 1559, 1631, 1691], // Channel 4
        [1189, 1261, 1350, 1431, 1505, 1574, 1648, 1708], // Channel 5
        [1202, 1274, 1363, 1444, 1520, 1589, 1665, 1725], // Channel 6
        [1215, 1287, 1376, 1457, 1535, 1604, 1682, 1742] // Channel 7
    ];

    private static readonly float[][] AllPassTunings =
    [
        [556, 441, 341, 225], // Channel 0
        [569, 454, 354, 238], // Channel 1
        [582, 467, 367, 251], // Channel 2
        [595, 480, 380, 264], // Channel 3
        [608, 493, 393, 277], // Channel 4
        [621, 506, 406, 290], // Channel 5
        [634, 519, 419, 303], // Channel 6
        [647, 532, 432, 316] // Channel 7
    ];

    private const float FixedGain = 0.015f;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlgorithmicReverbModifier" /> class.
    /// </summary>
    public AlgorithmicReverbModifier()
    {
        var numChannels = AudioEngine.Channels;

        // Initialize comb filters for each channel
        _combFilters = new CombFilter[numChannels][];
        _modulatedCombTuning = new float[numChannels * NumCombs];
        for (var channel = 0; channel < numChannels; channel++)
        {
            _combFilters[channel] = new CombFilter[NumCombs];
            for (var i = 0; i < NumCombs; i++)
            {
                var combTuning = CombTunings[channel % CombTunings.Length][i];
                _combFilters[channel][i] = new CombFilter((int)combTuning);
                _modulatedCombTuning[channel * NumCombs + i] = combTuning;
            }
        }

        // Initialize all-pass filters for each channel
        _allPassFilters = new AllPassFilter[numChannels][];
        for (var channel = 0; channel < numChannels; channel++)
        {
            _allPassFilters[channel] = new AllPassFilter[NumAllPasses];
            for (var i = 0; i < NumAllPasses; i++)
            {
                _allPassFilters[channel][i] =
                    new AllPassFilter((int)AllPassTunings[channel % AllPassTunings.Length][i]);
            }
        }

        var maxPreDelaySamples = (int)(AudioEngine.Instance.SampleRate * 0.1f); // Maximum pre-delay of 100ms
        _preDelayBuffers = new float[numChannels][];
        for (var channel = 0; channel < numChannels; channel++)
        {
            _preDelayBuffers[channel] = new float[maxPreDelaySamples];
        }

        _preDelayIndices = new int[numChannels];

        // Initialize LFO phases for modulation
        _lfoPhase = new float[numChannels];
        for (var channel = 0; channel < numChannels; channel++)
        {
            _lfoPhase[channel] = channel * (MathF.PI / numChannels);
        }

        UpdateParameters();
    }

    /// <summary>
    /// Gets or sets the wet mix amount. Clamped between 0 and 1.
    /// </summary>
    public float Wet
    {
        get => _wet;
        set => _wet = Math.Clamp(value, 0, 1);
    }

    /// <summary>
    /// Gets or sets the room size. Clamped between 0 and 1. Updates parameters when changed.
    /// </summary>
    public float RoomSize
    {
        get => _roomSize;
        set
        {
            _roomSize = Math.Clamp(value, 0, 1);
            UpdateParameters();
        }
    }

    /// <summary>
    /// Gets or sets the damping factor. Clamped between 0 and 1. Updates parameters when changed.
    /// </summary>
    public float Damp
    {
        get => _damp;
        set
        {
            _damp = Math.Clamp(value, 0, 1);
            UpdateParameters();
        }
    }

    /// <summary>
    /// Gets or sets the stereo width. Clamped between 0 and 1.
    /// </summary>
    public float Width
    {
        get => _width;
        set => _width = Math.Clamp(value, 0, 1);
    }

    /// <summary>
    /// Gets or sets the pre-delay time in milliseconds. Clamped between 0 and 100ms.
    /// </summary>
    public float PreDelay
    {
        get => _preDelay;
        set
        {
            _preDelay = Math.Clamp(value, 0, 100);
            _preDelaySamples = (int)(_preDelay * AudioEngine.Instance.SampleRate / 1000f);
        }
    }

    /// <summary>
    /// Gets or sets the wet/dry mix ratio. Clamped between 0 and 1.
    /// </summary>
    public float Mix
    {
        get => _mix;
        set => _mix = Math.Clamp(value, 0, 1);
    }

    private void UpdateParameters()
    {
        var numChannels = AudioEngine.Channels;

        // Ensure filter arrays are the correct size
        if (_combFilters.Length != numChannels)
        {
            _combFilters = new CombFilter[numChannels][];
            _modulatedCombTuning = new float[numChannels * NumCombs];
        }

        if (_allPassFilters.Length != numChannels) 
            _allPassFilters = new AllPassFilter[numChannels][];

        // Initialize or reinitialize filters for each channel
        for (var channel = 0; channel < numChannels; channel++)
        {
            if (_combFilters[channel].Length != 0) 
                _combFilters[channel] = new CombFilter[NumCombs];

            for (var i = 0; i < NumCombs; i++)
            {
                var combTuning = CombTunings[channel % CombTunings.Length][i];
                _combFilters[channel][i] = new CombFilter((int)combTuning);
                _modulatedCombTuning[channel * NumCombs + i] = combTuning;
                _combFilters[channel][i].Feedback = _roomSize;
                _combFilters[channel][i].Damp = _damp;
            }

            if (_allPassFilters[channel].Length != 0) _allPassFilters[channel] = new AllPassFilter[NumAllPasses];

            for (var i = 0; i < NumAllPasses; i++)
            {
                _allPassFilters[channel][i] =
                    new AllPassFilter((int)AllPassTunings[channel % AllPassTunings.Length][i]);
            }
        }

        // Reinitialize pre-delay buffers if necessary
        var maxPreDelaySamples = (int)(AudioEngine.Instance.SampleRate * 0.1f); // Maximum pre-delay of 100ms
        if (_preDelayBuffers.Length != numChannels ||
            (_preDelayBuffers.Length > 0 && _preDelayBuffers[0].Length != maxPreDelaySamples))
        {
            _preDelayBuffers = new float[numChannels][];
            for (var channel = 0; channel < numChannels; channel++)
            {
                _preDelayBuffers[channel] = new float[maxPreDelaySamples];
            }
        }

        _preDelayIndices = new int[numChannels];

        // Reinitialize LFO phases if necessary
        if (_lfoPhase.Length != numChannels)
        {
            var newLfoPhase = new float[numChannels];
            for (var channel = 0; channel < numChannels && channel < _lfoPhase.Length; channel++)
            {
                newLfoPhase[channel] = _lfoPhase[channel]; // Copy existing phases
            }

            for (var channel = _lfoPhase.Length; channel < numChannels; channel++)
            {
                newLfoPhase[channel] = channel * (MathF.PI / numChannels); // Initialize new phases
            }

            _lfoPhase = newLfoPhase;
        }
    }

    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel)
    {
        // Ensure channel index is within bounds
        if (channel < 0 || channel >= AudioEngine.Channels)
            throw new ArgumentOutOfRangeException(nameof(channel), "Channel index out of range.");

        // Calculate LFO value for modulation
        var lfo = MathF.Sin(_lfoPhase[channel]) * ModulationDepth;

        // Update LFO phase
        _lfoPhase[channel] += 2 * MathF.PI * ModulationRate / AudioEngine.Instance.SampleRate;
        if (_lfoPhase[channel] > MathF.PI)
            _lfoPhase[channel] -= 2 * MathF.PI;

        var input = sample * FixedGain;

        // Apply pre-delay
        _preDelayBuffers[channel][_preDelayIndices[channel]] = input;
        input = _preDelayBuffers[channel][
            (_preDelayIndices[channel] - _preDelaySamples + _preDelayBuffers[channel].Length) %
            _preDelayBuffers[channel].Length];

        float earlyReflectionsOutput = 0;
        float reverbTailOutput = 0;

        // Process comb filters with modulation
        for (var i = 0; i < NumCombs; i++)
        {
            // Modulate comb filter delay lengths
            var modulatedDelay = _modulatedCombTuning[channel * NumCombs + i] * (1 + lfo);
            _combFilters[channel][i].SetDelay((int)modulatedDelay);

            var combOutput = _combFilters[channel][i].Process(input);
            if (i < NumCombs / 2)
                earlyReflectionsOutput += combOutput; // Sum the first half for early reflections
            reverbTailOutput += combOutput;
        }

        // Process all-pass filters
        for (var i = 0; i < NumAllPasses; i++)
        {
            reverbTailOutput = _allPassFilters[channel][i].Process(reverbTailOutput);
        }

        _preDelayIndices[channel] = (_preDelayIndices[channel] + 1) % _preDelayBuffers[channel].Length;

        // Mix early reflections and reverb tail
        var mixedOutput = earlyReflectionsOutput * (1 - _mix) + reverbTailOutput * _mix;

        // Apply stereo width (simplified for multichannel) and wet/dry mix
        var spread = _width * (channel - (AudioEngine.Channels - 1) / 2f) / (AudioEngine.Channels - 1);

        return sample * (1 - _wet) + mixedOutput * _wet * (1 - spread);
    }

    private class CombFilter
    {
        private float[]? _buffer;
        private int _bufferIndex;
        private float _feedback;
        private float _damp1;
        private float _damp2;
        private float _lastOut;
        private int _delay;

        public CombFilter(int delay)
        {
            SetDelay(delay);
            _feedback = 0.5f;
            _damp1 = 0.5f;
            _damp2 = 0.5f;
        }

        public float Feedback
        {
            get => _feedback;
            set => _feedback = value;
        }

        public float Damp
        {
            get => _damp1;
            set
            {
                _damp1 = value;
                _damp2 = 1 - value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float input)
        {
            var output = _buffer![_bufferIndex];
            _lastOut = (output * _damp2) + (_lastOut * _damp1);
            _buffer[_bufferIndex] = input + _lastOut * _feedback;
            _bufferIndex = (_bufferIndex + 1) % _buffer.Length;
            return output;
        }

        public void SetDelay(int delay, bool resetBuffer = true)
        {
            // Clamp the delay to a reasonable range
            delay = Math.Clamp(delay, 1, int.MaxValue);

            if (_buffer != null && delay == _buffer.Length) 
                return;
            
            _buffer = new float[delay];
            _delay = delay;
            if (resetBuffer)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _bufferIndex = 0;
            }
        }
    }
    
    private class AllPassFilter(int delay)
    {
        private readonly float[] _buffer = new float[delay];
        private int _bufferIndex;
        private const float Feedback = 0.5f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float input)
        {
            var buffered = _buffer[_bufferIndex];
            var output = -input + buffered;
            _buffer[_bufferIndex] = input + buffered * Feedback;
            _bufferIndex = (_bufferIndex + 1) % _buffer.Length;
            return output;
        }
    }
}