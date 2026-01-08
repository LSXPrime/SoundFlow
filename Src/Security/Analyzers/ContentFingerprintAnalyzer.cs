using System.Numerics;
using SoundFlow.Abstracts;
using SoundFlow.Security.Configuration;
using SoundFlow.Security.Models;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Security.Analyzers;

/// <summary>
/// Analyzes audio content to generate robust acoustic fingerprints for identification.
/// Uses spectral peak analysis and combinatorial hashing.
/// </summary>
public sealed class ContentFingerprintAnalyzer : AudioAnalyzer
{
    private readonly FingerprintConfiguration _config;
    private readonly List<FingerprintHash> _hashes = [];
    
    // Buffering state
    private readonly float[] _ringBuffer;
    private int _ringBufferPos;
    private int _totalFramesProcessed;
    
    // FFT state
    private readonly Complex[] _fftBuffer;
    private readonly float[] _window;
    private readonly int _hopSize;

    // Peak tracking
    // Index = time offset (frame index), Value = List of peak frequency bin indices in that frame.
    private readonly Dictionary<int, List<int>> _spectralPeaksHistory = new();

    /// <inheritdoc />
    public override string Name { get; set; } = "Content Fingerprint Analyzer";

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentFingerprintAnalyzer"/> class.
    /// </summary>
    /// <param name="format">The audio format.</param>
    /// <param name="config">Configuration options. If null, defaults will be used.</param>
    public ContentFingerprintAnalyzer(AudioFormat format, FingerprintConfiguration? config = null) : base(format)
    {
        _config = config ?? new FingerprintConfiguration();
        
        if (!MathHelper.IsPowerOfTwo(_config.FftSize))
            throw new ArgumentException("FFT size must be a power of 2.");

        _fftBuffer = new Complex[_config.FftSize];
        _window = MathHelper.HanningWindow(_config.FftSize);
        _ringBuffer = new float[_config.FftSize];
        
        _hopSize = _config.FftSize / _config.OverlapFactor;
    }

    /// <summary>
    /// Gets the complete list of hashes generated so far.
    /// </summary>
    public IReadOnlyList<FingerprintHash> GetGeneratedHashes()
    {
        lock (_hashes)
        {
            return new List<FingerprintHash>(_hashes);
        }
    }

    /// <inheritdoc />
    protected override void Analyze(ReadOnlySpan<float> buffer, int channels)
    {
        // Downmix to mono and fill ring buffer
        for (var i = 0; i < buffer.Length; i += channels)
        {
            // Perform simple average downmix for channel agnosticism
            float monoSample = 0;
            for (var c = 0; c < channels; c++)
            {
                monoSample += buffer[i + c];
            }
            monoSample /= channels;

            _ringBuffer[_ringBufferPos] = monoSample;
            _ringBufferPos++;

            // When buffer is full, process frame
            if (_ringBufferPos >= _config.FftSize)
            {
                ProcessFrame();
                
                // Shift buffer by hop size to prepare for next overlap
                var remaining = _config.FftSize - _hopSize;
                Array.Copy(_ringBuffer, _hopSize, _ringBuffer, 0, remaining);
                _ringBufferPos = remaining;
            }
        }
    }

    /// <summary>
    /// Processes a single FFT frame: Windowing, FFT, Peak Extraction, and Hashing.
    /// </summary>
    private void ProcessFrame()
    {
        for (var i = 0; i < _config.FftSize; i++)
        {
            _fftBuffer[i] = new Complex(_ringBuffer[i] * _window[i], 0);
        }

        MathHelper.Fft(_fftBuffer);

        var peaks = ExtractPeaks();
        
        // Only store if we found peaks, optimization for silence
        if (peaks.Count > 0)
        {
            _spectralPeaksHistory[_totalFramesProcessed] = peaks;
            GenerateHashesForFrame(_totalFramesProcessed);
        }

        // Cleanup old history to prevent memory leak
        var historyHorizon = _totalFramesProcessed - _config.TargetZoneSize - 1;
        _spectralPeaksHistory.Remove(historyHorizon);

        _totalFramesProcessed++;
    }

    /// <summary>
    /// Identifies significant spectral peaks using adaptive thresholding and band limits.
    /// </summary>
    private List<int> ExtractPeaks()
    {
        var peaks = new List<int>();
        var binCount = _config.FftSize / 2;
        var frequencyResolution = (float)Format.SampleRate / _config.FftSize;

        // Calculate start and end bins based on frequency limits
        var minBin = (int)(_config.MinFrequency / frequencyResolution);
        var maxBin = (int)(_config.MaxFrequency / frequencyResolution);
        minBin = Math.Max(1, minBin); // Skip DC
        maxBin = Math.Min(binCount - 2, maxBin);

        // Calculate local average magnitude for adaptive thresholding
        double totalMag = 0;
        var count = 0;
        for (var i = minBin; i <= maxBin; i++)
        {
            totalMag += _fftBuffer[i].Magnitude;
            count++;
        }
        var averageMag = count > 0 ? totalMag / count : 0;
        
        // Adaptive threshold must be higher than floor and higher than local average * multiplier
        var threshold = Math.Max(_config.MinPeakMagnitude, averageMag * _config.AdaptiveThresholdMultiplier);

        // Divide frequency range into bands to ensure uniform peak distribution
        const int bandCount = 4; 
        var bandWidth = (maxBin - minBin) / bandCount;

        for (var b = 0; b < bandCount; b++)
        {
            var bandStart = minBin + b * bandWidth;
            var bandEnd = bandStart + bandWidth;
            
            var maxBandMag = 0.0;
            var maxBandBin = -1;

            for (var i = bandStart; i < bandEnd; i++)
            {
                var mag = _fftBuffer[i].Magnitude;

                // Check basic magnitude threshold
                if (mag < threshold) continue;

                // Check local maxima condition
                if (!(mag > _fftBuffer[i - 1].Magnitude) || !(mag > _fftBuffer[i + 1].Magnitude) ||
                    !(mag > maxBandMag)) continue;
                maxBandMag = mag;
                maxBandBin = i;
            }

            if (maxBandBin != -1)
            {
                peaks.Add(maxBandBin);
            }
        }

        return peaks;
    }

    /// <summary>
    /// Generates hashes by pairing peaks from the current frame with peaks from previous frames.
    /// </summary>
    private void GenerateHashesForFrame(int currentFrameIndex)
    {
        if (!_spectralPeaksHistory.TryGetValue(currentFrameIndex, out var targets) || targets.Count == 0) return;

        // Look back in time for Anchors within the target zone
        for (var t = 1; t <= _config.TargetZoneSize; t++)
        {
            var anchorFrameIndex = currentFrameIndex - t;
            if (!_spectralPeaksHistory.TryGetValue(anchorFrameIndex, out var anchors)) continue;

            foreach (var anchorBin in anchors)
            {
                foreach (var targetBin in targets)
                {
                    // Construct 32-bit Hash:
                    // 12 bits: Anchor Frequency Bin (0-4095)
                    // 12 bits: Target Frequency Bin (0-4095)
                    // 8 bits:  Delta Time (frames) (0-255)
                    
                    var h = (uint)((anchorBin & 0xFFF) << 20 | (targetBin & 0xFFF) << 8 | (t & 0xFF));
                    var hashEntry = new FingerprintHash(h, anchorFrameIndex);
                    lock (_hashes)
                    {
                        _hashes.Add(hashEntry);
                    }
                }
            }
        }
    }
}