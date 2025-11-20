using System.Numerics;
using SoundFlow.Abstracts;
using SoundFlow.Utils;

namespace SoundFlow.Modifiers;

/// <summary> 
/// An advanced vocal extraction effect that supports Mono, Stereo, and Surround formats.
/// </summary>
/// <remarks> 
/// <para>
/// For Stereo pairs (e.g., L/R), this uses Spatial Isolation (Mid-Side subtraction) 
/// to remove instruments panned to the sides.
/// </para>
/// <para>
/// For Mono or isolated channels (e.g., Center in 5.1), it falls back to Spectral Gating 
/// and Bandpass filtering to suppress noise and non-vocal frequencies.
/// </para>
/// </remarks>
public class VocalExtractorModifier : SoundModifier
{
    /// <inheritdoc/>
    public override string Name { get; set; } = "Vocal Extractor Modifier";

    private readonly int _sampleRate;
    private float _minFrequency;
    private float _maxFrequency;
    private bool _maskDirty = true;
    private readonly float[] _frequencyMask;
    private readonly float[] _window;

    /// <summary>
    /// Gain multiplier to compensate for windowing and subtraction loss.
    /// </summary>
    private const float MakeupGain = 3.5f;

    /// <summary>
    /// The penalty factor for stereo width. 
    /// Higher values (e.g., 2.0 - 3.0) more aggressively remove instruments panned to the sides.
    /// </summary>
    private const float StereoPenalty = 2.5f;

    /// <summary>
    /// The spectral gating threshold. 
    /// Frequencies quieter than this percentage of the frame's peak volume are suppressed.
    /// </summary>
    private const float BackgroundThreshold = 0.15f;

    /// <summary> 
    /// The lower bound of the frequency range to preserve (in Hz). 
    /// Set to ~100Hz to keep the fundamental vocal frequencies (warmth) while cutting sub-bass.
    /// </summary> 
    public float MinFrequency
    {
        get => _minFrequency;
        set
        {
            if (Math.Abs(_minFrequency - value) < 0.01f) return;
            _minFrequency = value;
            _maskDirty = true;
        }
    }

    /// <summary> 
    /// The upper bound of the frequency range to preserve (in Hz). 
    /// </summary> 
    public float MaxFrequency
    {
        get => _maxFrequency;
        set
        {
            if (Math.Abs(_maxFrequency - value) < 0.01f) return;
            _maxFrequency = value;
            _maskDirty = true;
        }
    }

    /// <summary> 
    /// The size of the Fast Fourier Transform (FFT) window. 
    /// </summary>
    public int FftSize { get; private set; }

    /// <summary> 
    /// The hop size of the FFT. 
    /// </summary> 
    public int HopSize { get; private set; }

    /// <summary>
    /// Holds the processing state for a single audio channel.
    /// </summary>
    private class ChannelState(int fftSize)
    {
        public readonly List<float> Input = new(fftSize * 2);
        public readonly List<float> Output = new(fftSize * 2);
        public readonly float[] Overlap = new float[fftSize];
    }

    private ChannelState[] _channelStates = [];

    /// <summary>
    /// Constructs a new instance of <see cref="VocalExtractorModifier"/>.
    /// </summary>
    /// <param name="sampleRate">The audio sample rate in Hz.</param>
    /// <param name="minFrequency">The lower bound of the frequency range. Defaults to 100Hz to preserve vocal body.</param>
    /// <param name="maxFrequency">The upper bound of the frequency range. Defaults to 10000Hz.</param>
    /// <param name="fftSize">The size of the FFT window. Defaults to 4096 for better bass resolution.</param>
    /// <param name="hopSize">The hop size of the FFT. Defaults to 1024.</param>
    public VocalExtractorModifier(int sampleRate, float minFrequency = 100f, float maxFrequency = 10000f,
        int fftSize = 4096, int hopSize = 1024)
    {
        _sampleRate = sampleRate;
        _minFrequency = minFrequency;
        _maxFrequency = maxFrequency;
        FftSize = fftSize;
        HopSize = hopSize;

        // Initialize Hamming window
        _window = new float[FftSize];
        for (var i = 0; i < FftSize; i++)
        {
            _window[i] = (float)(0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
        }

        _frequencyMask = new float[FftSize / 2 + 1];
        UpdateFrequencyMask();
    }

    /// <inheritdoc />
    public override void Process(Span<float> buffer, int channels)
    {
        if (!Enabled || channels <= 0) return;

        // Ensure we have states for every channel
        if (_channelStates.Length != channels)
        {
            _channelStates = new ChannelState[channels];
            for (var i = 0; i < channels; i++)
            {
                _channelStates[i] = new ChannelState(FftSize);
            }
        }

        if (_maskDirty)
        {
            UpdateFrequencyMask();
            _maskDirty = false;
        }

        var frameCount = buffer.Length / channels;

        // Load input data into channel states
        for (var i = 0; i < frameCount; i++)
        {
            for (var c = 0; c < channels; c++)
            {
                _channelStates[c].Input.Add(buffer[i * channels + c]);
            }
        }

        // Process channels in pairs where possible (for spatial isolation), 
        // or individually for odd channels (mono fallback).
        for (var c = 0; c < channels; c += 2)
        {
            // Check if we have a pair (e.g., L and R) or a leftover single channel
            bool isStereoPair = (c + 1) < channels;

            if (isStereoPair)
            {
                ProcessPair(_channelStates[c], _channelStates[c + 1]);
            }
            else
            {
                ProcessMono(_channelStates[c]);
            }
        }

        // Write output data back to buffer
        for (var i = 0; i < frameCount; i++)
        {
            for (var c = 0; c < channels; c++)
            {
                var state = _channelStates[c];
                if (state.Output.Count > 0)
                {
                    buffer[i * channels + c] = state.Output[0];
                    state.Output.RemoveAt(0);
                }
                else
                {
                    buffer[i * channels + c] = 0f;
                }
            }
        }
    }

    /// <summary>
    /// Processes two channels simultaneously using Spatial Isolation (Mid-Side).
    /// Best for L/R pairs in Stereo or Quad setups.
    /// </summary>
    private void ProcessPair(ChannelState stateL, ChannelState stateR)
    {
        var bufL = new Complex[FftSize];
        var bufR = new Complex[FftSize];

        // Process as long as both have enough data
        while (stateL.Input.Count >= FftSize && stateR.Input.Count >= FftSize)
        {
            // Windowing
            for (var i = 0; i < FftSize; i++)
            {
                bufL[i] = new Complex(stateL.Input[i] * _window[i], 0);
                bufR[i] = new Complex(stateR.Input[i] * _window[i], 0);
            }

            MathHelper.Fft(bufL);
            MathHelper.Fft(bufR);

            // Calculate Peak for Gating
            double framePeak = 0;
            for (var i = 0; i < FftSize / 2 + 1; i++)
            {
                var mag = (bufL[i].Magnitude + bufR[i].Magnitude) * 0.5;
                if (mag > framePeak) framePeak = mag;
            }
            var gateThreshold = framePeak * BackgroundThreshold;

            // Spectral Processing
            for (var i = 0; i < FftSize / 2 + 1; i++)
            {
                // Frequency Mask
                if (_frequencyMask[i] <= 0)
                {
                    ZeroBin(bufL, bufR, i);
                    continue;
                }

                // Spatial Isolation Logic
                var mid = (bufL[i] + bufR[i]) * 0.5;
                var side = (bufL[i] - bufR[i]) * 0.5;

                var magMid = mid.Magnitude;
                var magSide = side.Magnitude;

                // Subtract Side energy from Mid energy
                var vocalMag = magMid - (magSide * StereoPenalty);
                if (vocalMag < 0) vocalMag = 0;

                // Spectral Gating
                if (vocalMag < gateThreshold) vocalMag *= 0.1;

                // Reconstruction (Mono Phase)
                var result = Complex.FromPolarCoordinates(vocalMag, mid.Phase);
                
                ApplyResult(bufL, bufR, result, i);
            }

            FinalizeFrame(bufL, bufR, stateL, stateR);
        }
    }

    /// <summary>
    /// Processes a single channel using Bandpass and Spectral Gating only.
    /// Used for Mono inputs, or the Center/LFE channels in Surround setups.
    /// </summary>
    private void ProcessMono(ChannelState state)
    {
        var buf = new Complex[FftSize];

        while (state.Input.Count >= FftSize)
        {
            for (var i = 0; i < FftSize; i++)
            {
                buf[i] = new Complex(state.Input[i] * _window[i], 0);
            }

            MathHelper.Fft(buf);

            double framePeak = 0;
            for (var i = 0; i < FftSize / 2 + 1; i++)
            {
                if (buf[i].Magnitude > framePeak) framePeak = buf[i].Magnitude;
            }
            var gateThreshold = framePeak * BackgroundThreshold;

            for (var i = 0; i < FftSize / 2 + 1; i++)
            {
                if (_frequencyMask[i] <= 0)
                {
                    buf[i] = 0;
                    if (i > 0 && i < FftSize / 2) buf[FftSize - i] = 0;
                    continue;
                }

                var mag = buf[i].Magnitude;

                // Spectral Gating (No spatial subtraction possible in Mono)
                if (mag < gateThreshold) mag *= 0.1;

                var result = Complex.FromPolarCoordinates(mag, buf[i].Phase);
                buf[i] = result;

                if (i > 0 && i < FftSize / 2)
                {
                    buf[FftSize - i] = new Complex(result.Real, -result.Imaginary);
                }
            }

            MathHelper.InverseFft(buf);

            // Overlap-Add
            for (var i = 0; i < FftSize; i++)
            {
                state.Overlap[i] += (float)buf[i].Real * _window[i];
            }

            // Output
            for (var i = 0; i < HopSize; i++)
            {
                state.Output.Add(state.Overlap[i] * MakeupGain);
            }

            // Shift
            var remaining = FftSize - HopSize;
            Array.Copy(state.Overlap, HopSize, state.Overlap, 0, remaining);
            Array.Clear(state.Overlap, remaining, HopSize);
            state.Input.RemoveRange(0, HopSize);
        }
    }

    // Helper to reduce duplication in ProcessPair
    private void ZeroBin(Complex[] bufL, Complex[] bufR, int index)
    {
        bufL[index] = 0;
        bufR[index] = 0;
        if (index > 0 && index < FftSize / 2)
        {
            bufL[FftSize - index] = 0;
            bufR[FftSize - index] = 0;
        }
    }

    // Helper to apply result symmetrically in ProcessPair
    private void ApplyResult(Complex[] bufL, Complex[] bufR, Complex result, int index)
    {
        bufL[index] = result;
        bufR[index] = result;

        if (index > 0 && index < FftSize / 2)
        {
            var conj = new Complex(result.Real, -result.Imaginary);
            bufL[FftSize - index] = conj;
            bufR[FftSize - index] = conj;
        }
    }

    // Helper to handle IFFT and Overlap-Add for Stereo Pairs
    private void FinalizeFrame(Complex[] bufL, Complex[] bufR, ChannelState stateL, ChannelState stateR)
    {
        MathHelper.InverseFft(bufL);
        MathHelper.InverseFft(bufR);

        for (var i = 0; i < FftSize; i++)
        {
            stateL.Overlap[i] += (float)bufL[i].Real * _window[i];
            stateR.Overlap[i] += (float)bufR[i].Real * _window[i];
        }

        for (var i = 0; i < HopSize; i++)
        {
            stateL.Output.Add(stateL.Overlap[i] * MakeupGain);
            stateR.Output.Add(stateR.Overlap[i] * MakeupGain);
        }

        var remaining = FftSize - HopSize;
        
        Array.Copy(stateL.Overlap, HopSize, stateL.Overlap, 0, remaining);
        Array.Clear(stateL.Overlap, remaining, HopSize);
        stateL.Input.RemoveRange(0, HopSize);

        Array.Copy(stateR.Overlap, HopSize, stateR.Overlap, 0, remaining);
        Array.Clear(stateR.Overlap, remaining, HopSize);
        stateR.Input.RemoveRange(0, HopSize);
    }

    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel) => throw new NotSupportedException();

    private void UpdateFrequencyMask()
    {
        for (var i = 0; i < _frequencyMask.Length; i++)
        {
            var frequency = (double)i * _sampleRate / FftSize;
            _frequencyMask[i] = (frequency >= MinFrequency && frequency <= MaxFrequency) ? 1.0f : 0.0f;
        }
    }
}