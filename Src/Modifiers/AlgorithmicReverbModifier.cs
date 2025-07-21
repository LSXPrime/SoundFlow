using SoundFlow.Abstracts;
using System.Runtime.CompilerServices;
using SoundFlow.Structs;

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
    
    private readonly AudioFormat _format;

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
        [1215, 1287, 1376, 1457, 1535, 1604, 1682, 1742]  // Channel 7
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
        [647, 532, 432, 316]  // Channel 7
    ];

    private const float FixedGain = 0.015f;
    private const int MaxCombDelaySamples = 8000; // Max delay for comb filters (e.g. ~180ms @ 44.1kHz)

    /// <summary>
    /// Initializes a new instance of the <see cref="AlgorithmicReverbModifier" /> class.
    /// </summary>
    public AlgorithmicReverbModifier(AudioFormat format)
    {
        _format = format;
        _combFilters = [];
        _allPassFilters = [];
        _preDelayBuffers = [];
        _preDelayIndices = [];
        _lfoPhase = [];
        _modulatedCombTuning = [];
        UpdateParameters();
    }

    /// <summary>
    /// Gets or sets the wet mix amount. Clamped between 0 and 1.
    /// </summary>
    public float Wet
    {
        get => _wet;
        set => _wet = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Gets or sets the room size. Clamped between 0 and 1. Updates parameters when changed.
    /// </summary>
    public float RoomSize
    {
        get => _roomSize;
        set
        {
            _roomSize = Math.Clamp(value, 0f, 1f);
            UpdateReverbSettings();
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
            _damp = Math.Clamp(value, 0f, 1f);
            UpdateReverbSettings();
        }
    }

    /// <summary>
    /// Gets or sets the stereo width. Clamped between 0 and 1.
    /// </summary>
    public float Width
    {
        get => _width;
        set => _width = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Gets or sets the pre-delay time in milliseconds. Clamped between 0 and 100ms.
    /// </summary>
    public float PreDelay
    {
        get => _preDelay;
        set
        {
            _preDelay = Math.Clamp(value, 0f, 100f);
            _preDelaySamples = (int)(_preDelay * _format.SampleRate / 1000f);
        }
    }

    /// <summary>
    /// Gets or sets the wet/dry mix ratio. Clamped between 0 and 1.
    /// </summary>
    public float Mix
    {
        get => _mix;
        set => _mix = Math.Clamp(value, 0f, 1f);
    }

    private void UpdateReverbSettings()
    {
        foreach (var filter in _combFilters)
        {
            if (filter == null) continue;
            foreach (var innerFilter in filter)
            {
                if (innerFilter != null)
                {
                    innerFilter.Feedback = _roomSize;
                    innerFilter.Damp = _damp;
                }
            }
        }
    }

    private void UpdateParameters()
    {
        var numChannels = _format.Channels;
        var structureChanged = false;

        // Ensure filter arrays are the correct size
        if (_combFilters.Length != numChannels)
        {
            _combFilters = new CombFilter[numChannels][];
            _modulatedCombTuning = new float[numChannels * NumCombs];
            structureChanged = true;
        }

        if (_allPassFilters.Length != numChannels)
        {
            _allPassFilters = new AllPassFilter[numChannels][];
            structureChanged = true;
        }

        for (var channel = 0; channel < numChannels; channel++)
        {
            if (structureChanged || _combFilters[channel] == null || _combFilters[channel].Length != NumCombs)
            {
                var newChannelCombs = new CombFilter[NumCombs];
                for (var i = 0; i < NumCombs; i++)
                {
                    var combTuning = CombTunings[channel % CombTunings.Length][i];
                    _modulatedCombTuning[channel * NumCombs + i] = combTuning;
                    newChannelCombs[i] = new CombFilter((int)combTuning)
                    {
                        Feedback = _roomSize,
                        Damp = _damp
                    };
                }
                _combFilters[channel] = newChannelCombs;
            }
            else // Filters exist, ensure parameters are up-to-date if UpdateParameters was called for other reasons
            {
                 for (var i = 0; i < NumCombs; i++)
                 {
                    // Re-assign base tuning in case it could change (though CombTunings is static)
                    _modulatedCombTuning[channel * NumCombs + i] = CombTunings[channel % CombTunings.Length][i];
                    _combFilters[channel][i].Feedback = _roomSize;
                    _combFilters[channel][i].Damp = _damp;
                 }
            }

            if (structureChanged || _allPassFilters[channel] == null || _allPassFilters[channel].Length != NumAllPasses)
            {
                var newChannelAllPass = new AllPassFilter[NumAllPasses];
                for (var i = 0; i < NumAllPasses; i++)
                {
                    newChannelAllPass[i] = new AllPassFilter((int)AllPassTunings[channel % AllPassTunings.Length][i]);
                }
                _allPassFilters[channel] = newChannelAllPass;
            }
        }

        var maxPreDelaySamples = (int)(_format.SampleRate * 0.1f); // Max 100ms
        maxPreDelaySamples = Math.Max(1, maxPreDelaySamples); // Ensure at least 1 sample

        if (structureChanged || _preDelayBuffers.Length != numChannels || (_preDelayBuffers.Length > 0 && (_preDelayBuffers[0] == null || _preDelayBuffers[0].Length != maxPreDelaySamples)))
        {
            _preDelayBuffers = new float[numChannels][];
            for (var channel = 0; channel < numChannels; channel++)
            {
                _preDelayBuffers[channel] = new float[maxPreDelaySamples];
            }
        }

        if (structureChanged || _preDelayIndices.Length != numChannels)
        {
            _preDelayIndices = new int[numChannels]; // Resets to 0
        }

        if (structureChanged || _lfoPhase.Length != numChannels)
        {
            var newLfoPhase = new float[numChannels];
            for (var channel = 0; channel < numChannels; channel++)
            {
                newLfoPhase[channel] = (numChannels > 0) ? channel * (MathF.PI / numChannels) : 0;
            }
            _lfoPhase = newLfoPhase;
        }

        // Update preDelaySamples based on current sample rate if not explicitly set yet
        if (_preDelaySamples == 0 && _preDelay > 0)
        {
             _preDelaySamples = (int)(_preDelay * _format.SampleRate / 1000f);
        }
    }

    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel)
    {
        if (channel < 0 || channel >= _format.Channels || channel >= _combFilters.Length) // Safety check
        {
            // This case should ideally not happen if AudioEngine.Channels is consistent
            // Or could return 'sample' to bypass processing for misconfigured channels
            return sample;
        }

        var lfo = MathF.Sin(_lfoPhase[channel]) * ModulationDepth;
        _lfoPhase[channel] += 2 * MathF.PI * ModulationRate / _format.SampleRate;
        if (_lfoPhase[channel] > MathF.PI)
            _lfoPhase[channel] -= 2 * MathF.PI;

        var input = sample * FixedGain;

        if (_preDelaySamples > 0 && _preDelayBuffers[channel].Length > 0)
        {
            _preDelayBuffers[channel][_preDelayIndices[channel]] = input;
            input = _preDelayBuffers[channel][(_preDelayIndices[channel] - _preDelaySamples + _preDelayBuffers[channel].Length) % _preDelayBuffers[channel].Length];
            _preDelayIndices[channel] = (_preDelayIndices[channel] + 1) % _preDelayBuffers[channel].Length;
        }


        float earlyReflectionsOutput = 0;
        float reverbTailOutput = 0;

        var channelCombs = _combFilters[channel];
        if (channelCombs == null) return sample; // Should not happen if UpdateParameters is correct

        for (var i = 0; i < NumCombs; i++)
        {
            var comb = channelCombs[i];
            if (comb == null) continue; // Should not happen

            var modulatedDelay = _modulatedCombTuning[channel * NumCombs + i] * (1 + lfo);
            comb.SetDelay((int)modulatedDelay);

            var combOutput = comb.Process(input);
            if (i < NumCombs / 2)
                earlyReflectionsOutput += combOutput;
            reverbTailOutput += combOutput;
        }

        var channelAllPasses = _allPassFilters[channel];
        if (channelAllPasses == null) return sample; // Should not happen

        for (var i = 0; i < NumAllPasses; i++)
        {
            var allPass = channelAllPasses[i];
            if (allPass == null) continue; // Should not happen
            reverbTailOutput = allPass.Process(reverbTailOutput);
        }

        var mixedOutput = earlyReflectionsOutput * (1 - _mix) + reverbTailOutput * _mix;

        var spread = 0f;
        if (_format.Channels > 1)
        {
            spread = _width * (channel - (_format.Channels - 1) / 2f) / (_format.Channels - 1);
        }

        return sample * (1 - _wet) + mixedOutput * _wet * (1 - spread);
    }

    private class CombFilter
    {
        private float[] _buffer;
        private int _bufferIndex;
        private float _feedback;
        private float _damp1;
        private float _damp2;
        private float _lastOut;

        public CombFilter(int initialDelay)
        {
            _feedback = 0.5f;
            _damp1 = 0.5f;
            _damp2 = 1f - _damp1;
            _lastOut = 0f;
            _bufferIndex = 0;
            // Initialize buffer robustly
            var clampedDelay = Math.Clamp(initialDelay, 1, MaxCombDelaySamples);
            _buffer = new float[clampedDelay];
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
                _damp2 = 1f - value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float input)
        {
            var output = _buffer[_bufferIndex];
            _lastOut = (output * _damp2) + (_lastOut * _damp1);
            _buffer[_bufferIndex] = input + _lastOut * _feedback;
            _bufferIndex = (_bufferIndex + 1) % _buffer.Length;
            return output;
        }

        public void SetDelay(int delay)
        {
            delay = Math.Clamp(delay, 1, MaxCombDelaySamples);

            if (_buffer.Length == delay)
                return;

            // Reallocate buffer. This is expensive if called frequently per sample.
            // For Freeverb-style LFO modulation on delay lines, a common approach is fixed-size buffer
            // and modulated read pointers, or accepting slight artifacts/state reset.
            _buffer = new float[delay];
            Array.Clear(_buffer, 0, _buffer.Length); // Clear new buffer
            _bufferIndex = 0; // Reset index
            _lastOut = 0f; // Reset filter state
        }
    }

    private class AllPassFilter
    {
        private readonly float[] _buffer;
        private int _bufferIndex;
        private const float Feedback = 0.5f;

        public AllPassFilter(int delay)
        {
            // Clamp delay to a reasonable range for AllPass filters
            delay = Math.Clamp(delay, 1, MaxCombDelaySamples / 2); // Typically shorter than combs
            _buffer = new float[delay];
            _bufferIndex = 0;
        }

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
