using System.Numerics;
using SoundFlow.Abstracts;
using SoundFlow.Interfaces;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Components;

/// <summary>
/// Detects voice activity in audio streams using spectral analysis.
/// This version includes hysteresis (hangover) to prevent rapid state changes.
/// </summary>
public class VoiceActivityDetector : AudioAnalyzer
{
    private readonly Queue<float> _sampleBuffer = new();
    private readonly int _fftSize;
    private readonly float[] _window;
    private bool _isVoiceActive;
    private float _energyThreshold;
    private int _speechLowBand = 300;
    private int _speechHighBand = 3400;
    private int _activationFrames;
    private int _hangoverFrames;
    private int _currentSpeechFrameCount;
    private int _currentSilenceFrameCount;
    private readonly float _frameDurationMs;

    /// <summary>
    /// Gets whether voice activity is currently detected.
    /// </summary>
    public bool IsVoiceActive
    {
        get => _isVoiceActive;
        private set
        {
            if (_isVoiceActive != value)
            {
                _isVoiceActive = value;
                SpeechDetected?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the energy threshold for voice detection.
    /// </summary>
    public float EnergyThreshold
    {
        get => _energyThreshold;
        set => _energyThreshold = value;
    }
    
    /// <summary>
    /// Gets or sets the time in milliseconds the signal must be considered speech before activation.
    /// Helps prevent short noise bursts from triggering the VAD.
    /// </summary>
    public float ActivationTimeMs { get; set; } = 30f;

    /// <summary>
    /// Gets or sets the time in milliseconds to keep the VAD active after the last speech frame.
    /// Prevents the VAD from deactivating during short pauses.
    /// </summary>
    public float HangoverTimeMs { get; set; } = 200f;

    /// <summary>
    /// Gets or sets the lower bound of the frequency range used for speech detection in Hz.
    /// </summary>
    public int SpeechLowBand
    {
        get => _speechLowBand;
        set => _speechLowBand = value;
    }

    /// <summary>
    /// Gets or sets the upper bound of the frequency range used for speech detection in Hz.
    /// </summary>
    public int SpeechHighBand
    {
        get => _speechHighBand;
        set => _speechHighBand = value;
    }

    /// <summary>
    /// Initializes a new voice activity detector.
    /// </summary>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    /// <param name="fftSize">FFT window size (must be power of two). A common value is 1024 for 48kHz audio.</param>
    /// <param name="energyThreshold">Detection energy threshold. This is highly dependent on your input signal level.</param>
    /// <param name="visualizer">Optional visualizer for debugging</param>
    /// <remarks>
    /// Increase FFT size for better frequency resolution.
    /// Decrease threshold for higher sensitivity.
    /// Use larger FFT sizes in low-noise environments.
    /// Calibrate threshold based on input levels.
    /// </remarks>
    public VoiceActivityDetector(AudioFormat format, int fftSize = 1024, float energyThreshold = 5f, IVisualizer? visualizer = null)
        : base(format, visualizer)
    {
        if (!MathHelper.IsPowerOfTwo(fftSize))
            throw new ArgumentException("FFT size must be a power of two", nameof(fftSize));

        _fftSize = fftSize;
        _energyThreshold = energyThreshold;
        _window = MathHelper.HammingWindow(fftSize);

        // I'm 50% overlap for continuous analysis, so process frames every _fftSize / 2 samples.
        _frameDurationMs = (_fftSize / 2.0f) / Format.SampleRate * 1000.0f;
    }

    /// <summary>
    /// Analyzes audio buffer for voice activity.
    /// </summary>
    protected override void Analyze(ReadOnlySpan<float> buffer, int channels)
    {
        AddSamplesToBuffer(buffer, channels);

        _activationFrames = (int)Math.Ceiling(ActivationTimeMs / _frameDurationMs);
        _hangoverFrames = (int)Math.Ceiling(HangoverTimeMs / _frameDurationMs);

        while (_sampleBuffer.Count >= _fftSize)
        {
            var frame = new float[_fftSize];
            // Create a snapshot for analysis without dequeuing everything immediately
            var frameSamples = _sampleBuffer.ToArray(); 
            for (var i = 0; i < _fftSize; i++)
                frame[i] = frameSamples[i];

            ApplyWindow(frame);
            var spectrum = ComputeSpectrum(frame);
            var energy = CalculateSpeechBandEnergy(spectrum);

            bool isCurrentFrameSpeech = energy > _energyThreshold;

            if (isCurrentFrameSpeech)
            {
                _currentSpeechFrameCount++;
                _currentSilenceFrameCount = 0;
            }
            else
            {
                _currentSilenceFrameCount++;
                _currentSpeechFrameCount = 0;
            }

            if (!IsVoiceActive && _currentSpeechFrameCount >= _activationFrames)
            {
                // We have enough consecutive speech frames to activate
                IsVoiceActive = true;
            }
            else if (IsVoiceActive && _currentSilenceFrameCount >= _hangoverFrames)
            {
                // We have enough consecutive silence frames to deactivate
                IsVoiceActive = false;
            }
            
            for (int i = 0; i < _fftSize / 2; i++)
            {
                _sampleBuffer.Dequeue();
            }
        }
    }

    private void AddSamplesToBuffer(ReadOnlySpan<float> buffer, int channels)
    {
        if (channels == 1)
        {
            foreach (var sample in buffer)
                _sampleBuffer.Enqueue(sample);
        }
        else
        {
            for (var i = 0; i < buffer.Length; i += channels)
            {
                float sum = 0;
                for (var ch = 0; ch < channels; ch++)
                    sum += buffer[i + ch];
                _sampleBuffer.Enqueue(sum / channels);
            }
        }
    }

    private void ApplyWindow(Span<float> frame)
    {
        for (var i = 0; i < _fftSize; i++)
            frame[i] *= _window[i];
    }

    private float[] ComputeSpectrum(float[] frame)
    {
        var complexFrame = new Complex[_fftSize];
        for (var i = 0; i < _fftSize; i++)
            complexFrame[i] = new Complex(frame[i], 0);

        MathHelper.Fft(complexFrame);
        
        var spectrum = new float[_fftSize / 2];
        // Start from 1 to ignore DC offset
        for (var i = 1; i < _fftSize / 2; i++)
        {
            // Power Spectrum = Magnitude^2
            var magnitude = (float)(complexFrame[i].Magnitude / _fftSize);
            spectrum[i] = magnitude * magnitude;
        }
        return spectrum;
    }
    
    private float CalculateSpeechBandEnergy(float[] spectrum)
    {
        var binSize = Format.SampleRate / (float)_fftSize;
        var lowBin = (int)(_speechLowBand / binSize);
        var highBin = (int)(_speechHighBand / binSize);
            
        highBin = Math.Min(highBin, spectrum.Length - 1);
            
        float energy = 0;
        for (var i = lowBin; i <= highBin; i++)
            energy += spectrum[i];
            
        return energy;
    }
    
    /// <summary>
    /// Occurs when voice activity state changes.
    /// </summary>
    public event Action<bool>? SpeechDetected;
}